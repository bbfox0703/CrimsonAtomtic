using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.Core;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// One pickable entity in the World Map dialog's "ping object" dropdown.
/// Flattens an <see cref="PositionedEntityRecord"/> into the minimal
/// shape the UI needs (a display label + the world coords). Carries the
/// per-language resolved names + raw key separately so the filter can
/// substring-match against any of them (English name, secondary name,
/// region name, raw numeric ID) — typing "Howling" hits Howling Hills
/// gimmicks, typing "1000583" hits that exact key, typing the Chinese
/// region name hits everything in that region.
/// </summary>
public sealed record EntityChoice(
    PositionKind Kind,
    string DisplayLabel,
    string? EnglishName,
    string? SecondaryName,
    string? RegionName,
    uint RawKey,
    double WorldX,
    double WorldZ,
    uint FieldInfoKey)
{
    /// <summary>
    /// Case-insensitive substring match against every field the user
    /// might reasonably type: the formatted label, each per-language
    /// name, the region name, the raw key as decimal. Empty filter =
    /// pass-through.
    /// </summary>
    public bool MatchesFilter(string filter)
    {
        if (string.IsNullOrEmpty(filter)) return true;
        if (DisplayLabel.Contains(filter, StringComparison.OrdinalIgnoreCase)) return true;
        if (EnglishName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true) return true;
        if (SecondaryName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true) return true;
        if (RegionName?.Contains(filter, StringComparison.OrdinalIgnoreCase) == true) return true;
        // Numeric key — users often paste the raw ID from CE / save dumps.
        if (RawKey != 0
            && RawKey.ToString(CultureInfo.InvariantCulture)
                     .Contains(filter, StringComparison.Ordinal))
        {
            return true;
        }
        return false;
    }
}

/// <summary>
/// VM for the Tools → World Map dialog (post-refactor: user-picked
/// basemap + single-object ping). Holds the loaded bitmap, the flat
/// entity list pulled from <see cref="ISaveLoader.ListFieldPositions"/>,
/// and the currently-pinged marker's display-canvas coordinates.
///
/// <para>
/// Coordinate flow: world (X, Z) — pulled from the save —
/// <see cref="WorldMapAffine.Canonical"/> → reference-pixel space
/// (5178×5240) → stretched to the <see cref="DisplaySide"/>² canvas.
/// The user's basemap image can be any size or aspect; we force-stretch
/// it to fill the same square canvas, so markers land on the right
/// in-world location regardless of the file's pixel dimensions.
/// </para>
/// </summary>
public sealed partial class WorldMapViewModel : ObservableObject
{
    private readonly IPlatformPaths _paths;
    private readonly WorldMapAffine _affine = WorldMapAffine.Canonical;
    private readonly List<EntityChoice> _allObjects;

    /// <summary>Loaded basemap bitmap. <c>null</c> when no map has been picked yet.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasMap), nameof(BasemapPathLabel))]
    private Bitmap? _basemap;

    /// <summary>
    /// Absolute filesystem path of the loaded basemap. Mirrored into
    /// <see cref="AppSettings.WorldMapPath"/> so the same file is
    /// auto-loaded on the next launch.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(BasemapPathLabel))]
    private string? _basemapPath;

    /// <summary>
    /// Edge length of the square display canvas. Set by the view from
    /// the host monitor's working-area short side (auto-fit). Drives
    /// every world-to-pixel projection plus the <c>Width</c>/<c>Height</c>
    /// of the <see cref="Basemap"/> Image element (force-stretched).
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DisplayWidth), nameof(DisplayHeight))]
    private double _displaySide = 800;

    public double DisplayWidth => DisplaySide;
    public double DisplayHeight => DisplaySide;

    public bool HasMap => Basemap is not null;

    public string BasemapPathLabel => string.IsNullOrEmpty(BasemapPath)
        ? "(no map selected)"
        : BasemapPath!;

    /// <summary>
    /// Filter text from the toolbar searchbox. Live-rebuilds
    /// <see cref="FilteredObjects"/> on change.
    /// </summary>
    [ObservableProperty]
    private string _objectFilter = string.Empty;

    /// <summary>
    /// Items visible in the object picker dropdown. Always a subset of
    /// the flat list built at construction time.
    /// </summary>
    public ObservableCollection<EntityChoice> FilteredObjects { get; } = new();

    /// <summary>
    /// Currently-pinged entity. <c>null</c> = no marker visible.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasPingedMarker), nameof(PingedMarkerLabel))]
    private EntityChoice? _selectedObject;

    /// <summary>Pinged marker's display-canvas X (Canvas.Left target).</summary>
    [ObservableProperty]
    private double _pingedMarkerX;

    /// <summary>Pinged marker's display-canvas Y (Canvas.Top target).</summary>
    [ObservableProperty]
    private double _pingedMarkerY;

    public bool HasPingedMarker => SelectedObject is not null;

    public string PingedMarkerLabel => SelectedObject is null
        ? string.Empty
        : string.Format(
            CultureInfo.InvariantCulture,
            "{0}  (X={1:F1}, Z={2:F1})",
            SelectedObject.DisplayLabel,
            SelectedObject.WorldX,
            SelectedObject.WorldZ);

    /// <summary>
    /// Cursor world-coordinate readout (under the status bar). Updated
    /// from the view's pointer-moved handler.
    /// </summary>
    [ObservableProperty]
    private string _cursorCoordsText = string.Empty;

    public WorldMapViewModel(
        ISaveLoader loader,
        IPlatformPaths paths,
        LocalizationProvider localization,
        Bitmap? initialBitmap,
        string? initialPath)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(paths);
        ArgumentNullException.ThrowIfNull(localization);

        _paths = paths;
        _basemap = initialBitmap;
        _basemapPath = initialPath;
        _allObjects = BuildEntityList(loader, localization);

        RebuildFilteredObjects();
    }

    /// <summary>
    /// Flatten <see cref="ISaveLoader.ListFieldPositions"/> into a
    /// dropdown-ready <see cref="EntityChoice"/> list with resolved
    /// per-language names. Resolves:
    /// <list type="bullet">
    ///   <item>GimmickInfoKey → display name via <c>gimmickinfo.pabgb</c>
    ///     + PALOC hash-hop at <c>lo32=0x200</c>.</item>
    ///   <item>Mercenary / active char CharacterKey → display name via
    ///     <c>characterinfo.pabgb</c> + PALOC at <c>lo32=0x30</c>.</item>
    ///   <item>FieldInfoKey → region name via <c>regioninfo.pabgb</c>
    ///     (internal name only — no PALOC chain for RegionKey).</item>
    /// </list>
    /// Falls back to raw numeric IDs when any of the catalogs isn't
    /// loaded (no game install configured) so the picker stays usable.
    /// </summary>
    private static List<EntityChoice> BuildEntityList(
        ISaveLoader loader, LocalizationProvider localization)
    {
        var positions = loader.ListFieldPositions(out _);
        var list = new List<EntityChoice>(positions.Count);
        foreach (var rec in positions)
        {
            // Region label (RegionKey is internal-name only across all
            // languages — same value in English / secondary columns, so
            // RegionName is a single string rather than a pair).
            var regionRaw = localization.ResolveByFieldTypeName(
                "RegionKey", rec.FieldInfoKey);
            var regionName = string.IsNullOrEmpty(regionRaw) ? null : regionRaw;

            string? englishName = null;
            string? secondaryName = null;
            string nameDisplay;
            uint rawKey;

            switch (rec.Kind)
            {
                case PositionKind.ActiveChar:
                    rawKey = rec.CharacterKey;
                    (englishName, secondaryName) = ResolveNamePair(
                        localization, "CharacterKey", rawKey);
                    nameDisplay = FormatNamePair(englishName, secondaryName)
                                  ?? "Active char";
                    nameDisplay = "Active char — " + nameDisplay;
                    break;
                case PositionKind.Mercenary:
                    rawKey = rec.CharacterKey;
                    (englishName, secondaryName) = ResolveNamePair(
                        localization, "CharacterKey", rawKey);
                    var mercName = FormatNamePair(englishName, secondaryName);
                    nameDisplay = mercName is not null
                        ? string.Format(CultureInfo.InvariantCulture,
                            "{0}  ·  Mercenary #{1}", mercName, rec.MercenaryNo)
                        : string.Format(CultureInfo.InvariantCulture,
                            "Mercenary #{0}", rec.MercenaryNo);
                    break;
                case PositionKind.Gimmick:
                    rawKey = rec.GimmickInfoKey;
                    (englishName, secondaryName) = ResolveNamePair(
                        localization, "GimmickInfoKey", rawKey);
                    var gimName = FormatNamePair(englishName, secondaryName);
                    nameDisplay = gimName is not null
                        ? string.Format(CultureInfo.InvariantCulture,
                            "{0}  ·  #{1}", gimName, rawKey)
                        : string.Format(CultureInfo.InvariantCulture,
                            "Gimmick #{0}", rawKey);
                    break;
                default:
                    rawKey = 0;
                    nameDisplay = "Unknown";
                    break;
            }

            var label = regionName is not null
                ? string.Format(CultureInfo.InvariantCulture,
                    "{0}  ·  [{1}]", nameDisplay, regionName)
                : nameDisplay;

            list.Add(new EntityChoice(
                Kind: rec.Kind,
                DisplayLabel: label,
                EnglishName: englishName,
                SecondaryName: secondaryName,
                RegionName: regionName,
                RawKey: rawKey,
                WorldX: rec.PosX,
                WorldZ: rec.PosZ,
                FieldInfoKey: rec.FieldInfoKey));
        }
        return list;
    }

    /// <summary>
    /// Resolve <paramref name="typeName"/> + <paramref name="key"/> into
    /// English + secondary names by re-using the LocalizationProvider's
    /// public resolver. Returns <c>(null, null)</c> when the catalog
    /// isn't loaded or the key has no entry.
    /// </summary>
    private static (string? English, string? Secondary) ResolveNamePair(
        LocalizationProvider localization, string typeName, uint key)
    {
        if (key == 0) return (null, null);
        // ResolveByFieldTypeName returns the pre-formatted "English / Secondary"
        // (or just one side, or empty). For filter matching we want the
        // raw per-language strings, not the joined display form. Re-derive
        // by calling each language separately via the existing low-level
        // PALOC + table-driven entry points the provider exposes.
        //
        // Simpler approach for now: split the formatted result back. The
        // formatter joins with " / " so a single split works for the
        // bilingual case. Single-language results have no " / " and land
        // entirely in English.
        var combined = localization.ResolveByFieldTypeName(typeName, key);
        if (string.IsNullOrEmpty(combined)) return (null, null);
        var sep = combined.IndexOf(" / ", StringComparison.Ordinal);
        if (sep < 0)
        {
            return (combined, null);
        }
        return (combined[..sep], combined[(sep + 3)..]);
    }

    /// <summary>
    /// Re-join an English + secondary pair into a single display string.
    /// Returns <c>null</c> when neither side resolves so callers can fall
    /// back to "Gimmick #1000583"-style numeric labels.
    /// </summary>
    private static string? FormatNamePair(string? english, string? secondary)
    {
        if (english is null && secondary is null) return null;
        if (english is not null && secondary is not null)
        {
            return string.Format(CultureInfo.InvariantCulture,
                "{0} / {1}", english, secondary);
        }
        return english ?? secondary;
    }

    /// <summary>
    /// Update <see cref="Basemap"/> + <see cref="BasemapPath"/> with the
    /// newly-picked file. Persists the path into <see cref="AppSettings"/>
    /// so the dialog auto-loads it on next launch. Called by the view's
    /// "Pick Map…" handler after the file picker resolves.
    /// </summary>
    public void SetBasemap(string path)
    {
        var bitmap = WorldMapBasemapService.TryLoad(path);
        if (bitmap is null)
        {
            return;
        }
        // Dispose the previous bitmap before swapping, otherwise the
        // file lock + native handle linger until GC. The PropertyChanged
        // for Basemap below pushes the new bitmap into the binding.
        Basemap?.Dispose();
        Basemap = bitmap;
        BasemapPath = path;
        RecomputeMarkerPosition();
        PersistPathToSettings(path);
    }

    private void PersistPathToSettings(string path)
    {
        var existing = AppSettingsStore.Load(_paths.LocalAppDataDirectory);
        AppSettingsStore.TrySave(
            _paths.LocalAppDataDirectory,
            existing with { WorldMapPath = path });
    }

    /// <summary>
    /// Clear the pinged marker. Bound to the toolbar's clear button +
    /// invoked when the user picks the empty filter result.
    /// </summary>
    [RelayCommand]
    private void ClearSelection()
    {
        SelectedObject = null;
    }

    // ── Filter wiring ────────────────────────────────────────────────

    partial void OnObjectFilterChanged(string value) => RebuildFilteredObjects();

    private void RebuildFilteredObjects()
    {
        FilteredObjects.Clear();
        foreach (var choice in _allObjects)
        {
            if (choice.MatchesFilter(ObjectFilter))
            {
                FilteredObjects.Add(choice);
            }
        }
    }

    // ── Marker projection ────────────────────────────────────────────

    partial void OnSelectedObjectChanged(EntityChoice? value) => RecomputeMarkerPosition();

    partial void OnDisplaySideChanged(double value) => RecomputeMarkerPosition();

    private void RecomputeMarkerPosition()
    {
        if (SelectedObject is null)
        {
            return;
        }
        var (px, py) = _affine.WorldToDisplayPixel(
            SelectedObject.WorldX, SelectedObject.WorldZ,
            DisplaySide, DisplaySide);
        PingedMarkerX = px;
        PingedMarkerY = py;
    }

    // ── Cursor readout ───────────────────────────────────────────────

    /// <summary>
    /// Convert a display-canvas pointer position back into world coords
    /// and update the status-bar readout. Reverses the display-stretch
    /// then applies <see cref="WorldMapAffine.PixelToWorld"/>.
    /// </summary>
    public void UpdateCursorCoords(double displayPx, double displayPy)
    {
        var refPx = displayPx * _affine.ReferenceWidth / DisplaySide;
        var refPy = displayPy * _affine.ReferenceHeight / DisplaySide;
        var (wx, wz) = _affine.PixelToWorld(refPx, refPy);
        CursorCoordsText = string.Format(
            CultureInfo.InvariantCulture,
            "X={0:F1}  Z={1:F1}",
            wx, wz);
    }
}

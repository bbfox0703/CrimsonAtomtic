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
/// shape the UI needs (a display label + the world coords).
/// </summary>
public sealed record EntityChoice(
    PositionKind Kind,
    string DisplayLabel,
    double WorldX,
    double WorldZ,
    uint FieldInfoKey)
{
    /// <summary>
    /// Returns <c>true</c> when <paramref name="filter"/> is contained
    /// in <see cref="DisplayLabel"/> (case-insensitive). Used by the
    /// toolbar filter textbox.
    /// </summary>
    public bool MatchesFilter(string filter)
        => string.IsNullOrEmpty(filter)
           || DisplayLabel.Contains(filter, StringComparison.OrdinalIgnoreCase);
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
        Bitmap? initialBitmap,
        string? initialPath)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(paths);

        _paths = paths;
        _basemap = initialBitmap;
        _basemapPath = initialPath;
        _allObjects = BuildEntityList(loader);

        RebuildFilteredObjects();
    }

    /// <summary>
    /// Flatten <see cref="ISaveLoader.ListFieldPositions"/> into a
    /// dropdown-ready <see cref="EntityChoice"/> list. Labels are
    /// stable per-kind so the user can scan the list by region /
    /// instance ID.
    /// </summary>
    private static List<EntityChoice> BuildEntityList(ISaveLoader loader)
    {
        var positions = loader.ListFieldPositions(out _);
        var list = new List<EntityChoice>(positions.Count);
        foreach (var rec in positions)
        {
            var label = rec.Kind switch
            {
                PositionKind.ActiveChar => "Active char",
                PositionKind.Mercenary => string.Format(
                    CultureInfo.InvariantCulture,
                    "Mercenary #{0}",
                    rec.MercenaryNo),
                PositionKind.Gimmick => string.Format(
                    CultureInfo.InvariantCulture,
                    "Gimmick {0}  (region {1})",
                    rec.GimmickInfoKey,
                    rec.FieldInfoKey),
                _ => "Unknown",
            };
            list.Add(new EntityChoice(
                rec.Kind, label, rec.PosX, rec.PosZ, rec.FieldInfoKey));
        }
        return list;
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

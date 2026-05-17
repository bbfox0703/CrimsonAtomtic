using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using CrimsonAtomtic.RustInterop;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.ViewModels;

/// <summary>
/// Marker kind for the world-map overlay. Mirrors
/// <see cref="PositionKind"/> but exposes the enum to XAML bindings.
/// </summary>
public enum WorldMapMarkerKind
{
    ActiveChar,
    Mercenary,
    Gimmick,
}

/// <summary>
/// One marker on the world map. Combines a positioned-entity record
/// (the data source) with its precomputed basemap-pixel position (so
/// the rendering layer can place + rotate it without re-running the
/// affine on every layout pass).
/// </summary>
public sealed class WorldMapMarker
{
    public WorldMapMarkerKind Kind { get; init; }

    /// <summary>World X (east-west, global frame). Display only.</summary>
    public double WorldX { get; init; }

    /// <summary>World Z (north-south, global frame). Display only.</summary>
    public double WorldZ { get; init; }

    /// <summary>
    /// Pixel X on the basemap, after the affine. Drives
    /// <c>Canvas.Left</c> in the AXAML.
    /// </summary>
    public double PixelX { get; init; }

    /// <summary>
    /// Pixel Y on the basemap, after the affine. Drives
    /// <c>Canvas.Top</c> in the AXAML.
    /// </summary>
    public double PixelY { get; init; }

    /// <summary>
    /// Rotation around Y axis in <b>degrees</b> (converted from the
    /// underlying radians for AXAML's <c>RotateTransform.Angle</c>).
    /// Already biased so 0° = north (the yaw arrow points up); see
    /// <see cref="WorldMapViewModel.YawRadiansToDegrees"/>.
    /// </summary>
    public double YawDegrees { get; init; }

    /// <summary>
    /// <c>_fieldInfoKey</c> from the underlying record. Drives the
    /// region-coloring overlay (one hue per region).
    /// </summary>
    public uint FieldInfoKey { get; init; }

    /// <summary>
    /// Short owner label for tooltips. Resolved via
    /// <see cref="LocalizationProvider"/>; falls back to a key-based
    /// description when the bridge can't name the entity.
    /// </summary>
    public string OwnerLabel { get; init; } = string.Empty;
}

/// <summary>
/// VM for the Tools → World Map window. Holds the basemap image path,
/// the marker list precomputed from <see cref="ISaveLoader.ListFieldPositions"/>,
/// and the small bits of UI state the window needs (visible-kind
/// filters, distance-tool mode, mouse-coord readout text).
///
/// <para>
/// Phase 1 scope: read-only display. Marker drag-to-edit + pixel-
/// perfect affine calibration are roadmap follow-ons.
/// </para>
/// </summary>
public sealed partial class WorldMapViewModel : ObservableObject
{
    private readonly LocalizationProvider _localization;
    private readonly WorldMapAffine _affine;

    public WorldMapAffine Affine => _affine;

    public string BasemapPath { get; }

    /// <summary>
    /// Pre-loaded basemap bitmap. AXAML binds <c>Image.Source</c>
    /// directly to this so we don't need a path → Bitmap converter
    /// (Avalonia 11's <c>Image.Source</c> requires an IImage, not a
    /// file-path string).
    /// </summary>
    public Bitmap Basemap { get; }

    /// <summary>
    /// Width of the basemap image in pixels. Drives the
    /// <c>Image.Width</c> + the <c>Canvas.Width</c> on the marker
    /// overlay so the two stay aligned under the scroll viewer's
    /// scale transform.
    /// </summary>
    public double BasemapWidth => _affine.PixelWidth;

    /// <summary>Height of the basemap image in pixels.</summary>
    public double BasemapHeight => _affine.PixelHeight;

    /// <summary>
    /// Every positioned-entity marker, after the affine + yaw
    /// conversion. Used for export + bulk operations; rendering layers
    /// bind to one of the three per-kind collections below so the
    /// per-kind visibility checkboxes can drop entire layers at once
    /// without per-item visibility bindings.
    /// </summary>
    public List<WorldMapMarker> Markers { get; } = new();

    /// <summary>Active-character markers (one per save). Bound by the AXAML.</summary>
    public ObservableCollection<WorldMapMarker> ActiveCharMarkers { get; } = new();

    /// <summary>Mercenary / mount markers. Bound by the AXAML.</summary>
    public ObservableCollection<WorldMapMarker> MercenaryMarkers { get; } = new();

    /// <summary>Field gimmick markers. Bound by the AXAML.</summary>
    public ObservableCollection<WorldMapMarker> GimmickMarkers { get; } = new();

    [ObservableProperty]
    private bool _showActiveChar = true;

    [ObservableProperty]
    private bool _showMercenaries = true;

    [ObservableProperty]
    private bool _showGimmicks = true;

    [ObservableProperty]
    private bool _showYawArrows = true;

    [ObservableProperty]
    private bool _useRegionColoring;

    /// <summary>
    /// Map zoom level. The window's <c>ScaleTransform.ScaleX/ScaleY</c>
    /// bind to this. Code-behind reads + writes it via the property
    /// (mouse wheel + export path) since the XAML compiler doesn't
    /// auto-generate fields for non-Control elements like
    /// <c>ScaleTransform</c>.
    /// </summary>
    [ObservableProperty]
    private double _zoomScale = 1.0;

    /// <summary>
    /// World-coordinate readout under the cursor, formatted as
    /// <c>"X=… Z=… (px=…,py=…)"</c>. Updated from the view's pointer-
    /// moved handler via <see cref="UpdateCursorWorldCoords"/>.
    /// </summary>
    [ObservableProperty]
    private string _cursorCoordsText = string.Empty;

    /// <summary>
    /// Distance-measurement state. <c>Idle</c> = picker disabled,
    /// <c>WaitingForFirst</c> = next click sets <see cref="DistancePoint1"/>,
    /// <c>WaitingForSecond</c> = next click sets <see cref="DistancePoint2"/>.
    /// </summary>
    public enum DistanceToolState { Idle, WaitingForFirst, WaitingForSecond }

    [ObservableProperty]
    private DistanceToolState _distanceMode = DistanceToolState.Idle;

    /// <summary>
    /// (PixelX, PixelY) of the first picked anchor. Use
    /// <see cref="HasDistancePoint1"/> to gate visibility — AXAML
    /// can't deref a nullable tuple cleanly across all binding paths.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DistancePoint1AsAvaloniaPoint))]
    private double _distancePoint1X;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DistancePoint1AsAvaloniaPoint))]
    private double _distancePoint1Y;

    [ObservableProperty]
    private bool _hasDistancePoint1;

    /// <summary>(PixelX, PixelY) of the second picked anchor.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DistancePoint2AsAvaloniaPoint))]
    private double _distancePoint2X;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(DistancePoint2AsAvaloniaPoint))]
    private double _distancePoint2Y;

    [ObservableProperty]
    private bool _hasDistancePoint2;

    /// <summary>
    /// Convenience accessor used by the line overlay: returns a
    /// <c>Point</c> shaped for <c>Line.StartPoint</c> / <c>EndPoint</c>.
    /// </summary>
    public Avalonia.Point DistancePoint1AsAvaloniaPoint =>
        new(DistancePoint1X, DistancePoint1Y);

    public Avalonia.Point DistancePoint2AsAvaloniaPoint =>
        new(DistancePoint2X, DistancePoint2Y);

    /// <summary>
    /// Human-readable distance label. Empty when no measurement is
    /// in progress; "First point picked…" after the first click; the
    /// final "Δ=… world units" after the second click.
    /// </summary>
    [ObservableProperty]
    private string _distanceText = string.Empty;

    /// <summary>
    /// Total marker count across all kinds. Drives the footer
    /// "N markers" readout — value is static post-construction since
    /// the underlying record set comes from a single
    /// <c>ListFieldPositions</c> snapshot.
    /// </summary>
    public int TotalMarkerCount => Markers.Count;

    /// <summary>
    /// Region color for a given <see cref="WorldMapMarker.FieldInfoKey"/>.
    /// Hashes the field-info key into the HSL hue space so different
    /// regions get visually distinct colors without us needing a curated
    /// region → color table. Used when <see cref="UseRegionColoring"/>
    /// is on; otherwise the kind's stock color is used.
    /// </summary>
    public static (byte R, byte G, byte B) RegionColor(uint fieldInfoKey)
    {
        // Map field_info_key → hue ∈ [0, 360). Use a 32→32 mix to avoid
        // adjacent keys landing on adjacent hues.
        var mixed = fieldInfoKey * 2654435761u;
        var hue = (mixed % 360u) / 360.0;
        // Saturation + lightness fixed at a comfortable contrast range.
        return HslToRgb(hue, 0.55, 0.55);
    }

    private static (byte R, byte G, byte B) HslToRgb(double h, double s, double l)
    {
        var c = (1 - Math.Abs((2 * l) - 1)) * s;
        var hp = h * 6.0;
        var x = c * (1 - Math.Abs((hp % 2) - 1));
        double r1 = 0, g1 = 0, b1 = 0;
        switch ((int)Math.Floor(hp))
        {
            case 0: r1 = c; g1 = x; break;
            case 1: r1 = x; g1 = c; break;
            case 2: g1 = c; b1 = x; break;
            case 3: g1 = x; b1 = c; break;
            case 4: r1 = x; b1 = c; break;
            default: r1 = c; b1 = x; break;
        }
        var m = l - (c / 2);
        return (
            (byte)Math.Round((r1 + m) * 255),
            (byte)Math.Round((g1 + m) * 255),
            (byte)Math.Round((b1 + m) * 255));
    }

    /// <summary>
    /// Per-kind counts surfaced in the filter checkboxes' labels.
    /// </summary>
    public int ActiveCharCount { get; private set; }
    public int MercenaryCount { get; private set; }
    public int GimmickCount { get; private set; }

    public WorldMapViewModel(
        ISaveLoader loader,
        LocalizationProvider localization,
        string basemapPath,
        WorldMapAffine affine)
    {
        ArgumentNullException.ThrowIfNull(loader);
        ArgumentNullException.ThrowIfNull(localization);
        ArgumentException.ThrowIfNullOrEmpty(basemapPath);

        _localization = localization;
        _affine = affine;
        BasemapPath = basemapPath;
        Basemap = new Bitmap(basemapPath);

        PopulateMarkers(loader);
    }

    private void PopulateMarkers(ISaveLoader loader)
    {
        var positions = loader.ListFieldPositions(out _);
        Markers.Clear();
        ActiveCharMarkers.Clear();
        MercenaryMarkers.Clear();
        GimmickMarkers.Clear();

        foreach (var rec in positions)
        {
            var (px, py) = _affine.WorldToPixel(rec.PosX, rec.PosZ);
            var kind = rec.Kind switch
            {
                PositionKind.ActiveChar => WorldMapMarkerKind.ActiveChar,
                PositionKind.Mercenary => WorldMapMarkerKind.Mercenary,
                PositionKind.Gimmick => WorldMapMarkerKind.Gimmick,
                _ => WorldMapMarkerKind.Gimmick,
            };
            var marker = new WorldMapMarker
            {
                Kind = kind,
                WorldX = rec.PosX,
                WorldZ = rec.PosZ,
                PixelX = px,
                PixelY = py,
                YawDegrees = YawRadiansToDegrees(rec.Yaw),
                FieldInfoKey = rec.FieldInfoKey,
                OwnerLabel = ResolveOwnerLabel(rec),
            };
            Markers.Add(marker);
            switch (kind)
            {
                case WorldMapMarkerKind.ActiveChar: ActiveCharMarkers.Add(marker); break;
                case WorldMapMarkerKind.Mercenary: MercenaryMarkers.Add(marker); break;
                case WorldMapMarkerKind.Gimmick: GimmickMarkers.Add(marker); break;
            }
        }

        ActiveCharCount = ActiveCharMarkers.Count;
        MercenaryCount = MercenaryMarkers.Count;
        GimmickCount = GimmickMarkers.Count;
        OnPropertyChanged(nameof(TotalMarkerCount));
        OnPropertyChanged(nameof(ActiveCharCount));
        OnPropertyChanged(nameof(MercenaryCount));
        OnPropertyChanged(nameof(GimmickCount));
    }

    /// <summary>
    /// Convert the underlying record's yaw (radians, Y-up) to AXAML's
    /// degree-based <c>RotateTransform.Angle</c>. We rotate around the
    /// marker's pixel position; 0° points north (image −Y), which means
    /// we subtract the in-game yaw from 0 then convert to degrees.
    /// </summary>
    private static double YawRadiansToDegrees(float yaw)
    {
        // In-game Y-up yaw: 0 rad = facing world +Z (north on the basemap,
        // which is the −Y direction in image coords). Z axis is flipped
        // by the affine, so the image-space arrow heading is the same
        // sign as the world-space yaw — just convert radians → degrees.
        return -yaw * (180.0 / Math.PI);
    }

    private static string ResolveOwnerLabel(PositionedEntityRecord rec)
    {
        return rec.Kind switch
        {
            PositionKind.ActiveChar => "Active character",
            PositionKind.Mercenary => $"Mercenary #{rec.MercenaryNo}",
            PositionKind.Gimmick => $"Gimmick {rec.GimmickInfoKey}",
            _ => "Unknown",
        };
    }

    /// <summary>
    /// Called from the view's pointer-moved handler. Converts the
    /// pointer-pixel coord on the basemap image back into world coords
    /// and updates the readout text.
    /// </summary>
    public void UpdateCursorWorldCoords(double px, double py)
    {
        var (wx, wz) = _affine.PixelToWorld(px, py);
        CursorCoordsText = string.Format(
            CultureInfo.InvariantCulture,
            "X={0:F1}  Z={1:F1}  (px={2:F0}, py={3:F0})",
            wx, wz, px, py);
    }

    /// <summary>
    /// Called from the view's pointer-pressed handler when the user is
    /// in distance-tool mode. Advances the state machine.
    /// </summary>
    public void HandleDistanceClick(double px, double py)
    {
        switch (DistanceMode)
        {
            case DistanceToolState.WaitingForFirst:
                DistancePoint1X = px;
                DistancePoint1Y = py;
                HasDistancePoint1 = true;
                HasDistancePoint2 = false;
                DistanceMode = DistanceToolState.WaitingForSecond;
                DistanceText = "First point picked — click the second point.";
                break;
            case DistanceToolState.WaitingForSecond:
                DistancePoint2X = px;
                DistancePoint2Y = py;
                HasDistancePoint2 = true;
                DistanceMode = DistanceToolState.Idle;
                UpdateDistanceLabel();
                break;
            default:
                break;
        }
    }

    private void UpdateDistanceLabel()
    {
        if (!HasDistancePoint1 || !HasDistancePoint2)
        {
            DistanceText = string.Empty;
            return;
        }
        var (w1x, w1z) = _affine.PixelToWorld(DistancePoint1X, DistancePoint1Y);
        var (w2x, w2z) = _affine.PixelToWorld(DistancePoint2X, DistancePoint2Y);
        var dx = w2x - w1x;
        var dz = w2z - w1z;
        var dist = Math.Sqrt((dx * dx) + (dz * dz));
        DistanceText = string.Format(
            CultureInfo.InvariantCulture,
            "Distance: {0:F1} world units (ΔX={1:F1}, ΔZ={2:F1})",
            dist, dx, dz);
    }

    /// <summary>
    /// Command bound to the toolbar's "Measure distance" button. Cycles
    /// the tool through Idle → WaitingForFirst → Idle (cancel mid-pick).
    /// </summary>
    [RelayCommand]
    private void ToggleDistanceTool()
    {
        if (DistanceMode == DistanceToolState.Idle)
        {
            DistanceMode = DistanceToolState.WaitingForFirst;
            HasDistancePoint1 = false;
            HasDistancePoint2 = false;
            DistanceText = "Click the first point on the map.";
        }
        else
        {
            DistanceMode = DistanceToolState.Idle;
            DistanceText = string.Empty;
            HasDistancePoint1 = false;
            HasDistancePoint2 = false;
        }
    }
}

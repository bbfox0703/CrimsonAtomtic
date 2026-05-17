using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform.Storage;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

/// <summary>
/// Tools → World Map window. Hosts the basemap image + marker overlay,
/// implements pan / zoom against the user's input, and runs the
/// distance-tool + PNG export pipeline. All persistent state lives on
/// the bound <see cref="WorldMapViewModel"/>; the code-behind owns
/// only transient view state (mid-drag pointer anchor + the scale
/// transform).
/// </summary>
public sealed partial class WorldMapWindow : Window
{
    // Pan: when the user drags with left mouse button held, record the
    // initial pointer position + scroll offset and stream offset updates
    // until release.
    private Point? _panAnchorPointerScreen;
    private Vector _panAnchorScrollOffset;
    private const double MinScale = 0.1;
    private const double MaxScale = 8.0;
    private const double WheelStepFactor = 1.15;

    public WorldMapWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    // ── Pan + zoom ──────────────────────────────────────────────────────

    private void OnMapPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (DataContext is not WorldMapViewModel vm) return;

        var pos = e.GetPosition(MapStage);

        // Distance tool wins over pan: when armed, a left click sets the
        // next anchor and consumes the event.
        if (vm.DistanceMode != WorldMapViewModel.DistanceToolState.Idle
            && e.GetCurrentPoint(MapStage).Properties.IsLeftButtonPressed)
        {
            vm.HandleDistanceClick(pos.X, pos.Y);
            e.Handled = true;
            return;
        }

        // Otherwise treat a left-button press as a pan grab. Middle
        // button works too — handy for trackpad users on Windows.
        var props = e.GetCurrentPoint(MapStage).Properties;
        if (props.IsLeftButtonPressed || props.IsMiddleButtonPressed)
        {
            _panAnchorPointerScreen = e.GetPosition(MapScroll);
            _panAnchorScrollOffset = MapScroll.Offset;
            e.Pointer.Capture(MapStage);
            e.Handled = true;
        }
    }

    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not WorldMapViewModel vm) return;

        // Always update the world-coord readout under the cursor.
        var posOnStage = e.GetPosition(MapStage);
        vm.UpdateCursorWorldCoords(posOnStage.X, posOnStage.Y);

        if (_panAnchorPointerScreen is { } anchor)
        {
            var current = e.GetPosition(MapScroll);
            var delta = current - anchor;
            // Drag-pan = scroll the viewport opposite to the pointer.
            // Clamp through ScrollViewer.Offset which auto-clips to
            // [0, Extent − Viewport].
            MapScroll.Offset = _panAnchorScrollOffset - delta;
        }
    }

    private void OnMapPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        if (_panAnchorPointerScreen is not null)
        {
            _panAnchorPointerScreen = null;
            e.Pointer.Capture(null);
            e.Handled = true;
        }
    }

    private void OnMapPointerWheel(object? sender, PointerWheelEventArgs e)
    {
        if (DataContext is not WorldMapViewModel vm) return;

        var step = e.Delta.Y > 0 ? WheelStepFactor : 1.0 / WheelStepFactor;
        var oldScale = vm.ZoomScale;
        var newScale = Math.Clamp(oldScale * step, MinScale, MaxScale);
        if (Math.Abs(newScale - oldScale) < 1e-6)
        {
            return;
        }

        // Zoom around the pointer position: keep the world-space pixel
        // under the cursor at the same screen location after the scale
        // change. Otherwise zoom drifts away from where the user is
        // looking, which feels broken.
        var pointerOnStage = e.GetPosition(MapStage);
        vm.ZoomScale = newScale;

        // After the scale change the same MapStage point maps to a
        // different screen offset. Adjust ScrollViewer.Offset so the
        // pointer-anchored pixel stays put. Projected position =
        // (pointerOnStage * newScale − scrollOffset); to keep it at
        // the original screen position, shift scrollOffset by
        // pointerOnStage * (newScale − oldScale).
        var dx = pointerOnStage.X * (newScale - oldScale);
        var dy = pointerOnStage.Y * (newScale - oldScale);
        MapScroll.Offset += new Vector(dx, dy);
        e.Handled = true;
    }

    // ── Export current view as PNG ──────────────────────────────────────

    /// <summary>
    /// "Export PNG" toolbar button. Renders the MapStage (basemap +
    /// markers + distance overlay, post-scale) to a
    /// <see cref="RenderTargetBitmap"/> at native resolution
    /// (basemap × current scale), then saves to user-chosen PNG via
    /// Avalonia's storage picker. No interactive UI thread blocking —
    /// the file write hops to a worker.
    /// </summary>
    private async void OnExportClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorldMapViewModel vm) return;

        var picked = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = (string?)this.FindResource("WorldMapExportPickerTitle") ?? "Export world map PNG",
            DefaultExtension = "png",
            SuggestedFileName = "crimson-worldmap.png",
            FileTypeChoices =
            [
                new FilePickerFileType("PNG image")
                {
                    Patterns = ["*.png"],
                    MimeTypes = ["image/png"],
                },
            ],
        });
        if (picked is null)
        {
            return;
        }

        // Render at native basemap resolution (ignore current zoom) so
        // the export is the same crisp image regardless of how far in
        // the user has zoomed. The marker layers + distance overlay
        // pick up the basemap-space coordinates we already use, so the
        // export composes correctly without a separate render pipeline.
        var basemapWidth = (int)vm.BasemapWidth;
        var basemapHeight = (int)vm.BasemapHeight;
        var pixelSize = new PixelSize(basemapWidth, basemapHeight);
        var dpi = new Vector(96, 96);

        // Snapshot the current scale, force-render at 1:1 for the
        // export, then restore. The user's interactive zoom level
        // shouldn't affect the file.
        var savedScale = vm.ZoomScale;
        vm.ZoomScale = 1.0;
        // Force the layout system to flush the scale change before
        // rendering — otherwise we capture the pre-restore frame.
        MapStage.InvalidateMeasure();
        MapStage.InvalidateArrange();
        MapStage.UpdateLayout();

        try
        {
            using var rtb = new RenderTargetBitmap(pixelSize, dpi);
            rtb.Render(MapStage);

            await using var stream = await picked.OpenWriteAsync();
            rtb.Save(stream);
        }
        finally
        {
            vm.ZoomScale = savedScale;
            MapStage.InvalidateMeasure();
            MapStage.InvalidateArrange();
        }
    }
}

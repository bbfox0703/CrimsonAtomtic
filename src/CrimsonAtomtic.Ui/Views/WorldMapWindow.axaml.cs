using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

/// <summary>
/// Tools → World Map dialog. The user supplies their own basemap image
/// (any size, any format Avalonia's <see cref="Avalonia.Media.Imaging.Bitmap"/>
/// ctor accepts); the dialog force-stretches it to a square display
/// canvas auto-fit to the host monitor and overlays a single marker for
/// the user-picked entity. World-to-pixel projection uses the canonical
/// <see cref="Services.WorldMapAffine.Canonical"/> constants.
/// </summary>
public sealed partial class WorldMapWindow : Window
{
    /// <summary>
    /// Fraction of the host monitor's short side used as the square
    /// map canvas's edge length. 0.85 leaves room for the surrounding
    /// chrome (toolbar + status bar + window title bar + taskbar).
    /// </summary>
    private const double ScreenFitFactor = 0.85;

    public WorldMapWindow()
    {
        InitializeComponent();
        Opened += OnWindowOpened;
        // The VM owns the user's basemap bitmap (~108 MB of native Skia
        // memory for the canonical map). Dispose it when the window
        // closes instead of abandoning it to the finalizer, so repeated
        // open/close cycles don't stack native allocations.
        Closed += OnWindowClosed;
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    private void OnWindowClosed(object? sender, System.EventArgs e)
    {
        (DataContext as System.IDisposable)?.Dispose();
    }

    /// <summary>
    /// Auto-fit the square map canvas to the host monitor on first
    /// open. <see cref="WorldMapViewModel.DisplaySide"/> drives every
    /// world-to-pixel projection, so this also re-positions any pinged
    /// marker via the VM's <c>OnDisplaySideChanged</c> partial method.
    /// </summary>
    private void OnWindowOpened(object? sender, System.EventArgs e)
    {
        if (DataContext is not WorldMapViewModel vm) return;
        var screen = Screens.ScreenFromVisual(this) ?? Screens.Primary;
        if (screen is null) return;

        // WorkingArea excludes the taskbar; safer than Bounds when the
        // user has a taskbar parked on top / bottom. Divide by scaling
        // so the canvas size is in device-independent pixels (matches
        // Width/Height bindings).
        var workArea = screen.WorkingArea;
        var scaling = screen.Scaling;
        var shortSideDip = Math.Min(workArea.Width, workArea.Height) / scaling;
        vm.DisplaySide = shortSideDip * ScreenFitFactor;

        // Size the window to fit the canvas plus toolbar/statusbar
        // padding. Approximate budget: 80 dip vertical chrome + 20 dip
        // horizontal margins.
        Width = vm.DisplaySide + 32;
        Height = vm.DisplaySide + 96;
    }

    /// <summary>
    /// Pointer-moved on the map canvas — update the world-coord status
    /// readout. Pointer position is in display-canvas pixels; the VM
    /// inverts the stretch + affine to derive world (X, Z).
    /// </summary>
    private void OnMapPointerMoved(object? sender, PointerEventArgs e)
    {
        if (DataContext is not WorldMapViewModel vm) return;
        var pos = e.GetPosition(MapStage);
        vm.UpdateCursorCoords(pos.X, pos.Y);
    }

    /// <summary>
    /// "Pick Map…" toolbar handler. Opens the platform file picker for
    /// any image file Avalonia can load, then pushes the result into
    /// the VM (which loads the bitmap + persists the path to settings).
    /// </summary>
    private async void OnPickMapClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not WorldMapViewModel vm) return;

        IStorageFolder? startLocation = null;
        if (!string.IsNullOrEmpty(vm.BasemapPath))
        {
            var dir = System.IO.Path.GetDirectoryName(vm.BasemapPath);
            if (!string.IsNullOrEmpty(dir) && System.IO.Directory.Exists(dir))
            {
                startLocation = await StorageProvider.TryGetFolderFromPathAsync(dir);
            }
        }

        var picked = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = (string?)this.FindResource("WorldMapPickMapTitle") ?? "Pick world map image",
            AllowMultiple = false,
            SuggestedStartLocation = startLocation,
            FileTypeFilter =
            [
                new FilePickerFileType("Image files")
                {
                    Patterns = ["*.png", "*.jpg", "*.jpeg", "*.webp", "*.bmp"],
                    MimeTypes = ["image/png", "image/jpeg", "image/webp", "image/bmp"],
                },
                FilePickerFileTypes.All,
            ],
        });
        if (picked.Count == 0) return;

        var path = picked[0].TryGetLocalPath();
        if (!string.IsNullOrEmpty(path))
        {
            vm.SetBasemap(path);
        }
    }
}

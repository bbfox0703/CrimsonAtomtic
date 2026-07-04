using Avalonia.Controls;
// Same Avalonia 12 dance as the other picker windows — SetTextAsync
// lives on ClipboardExtensions, not directly on IClipboard.
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class FindItemsWindow : Window
{
    public FindItemsWindow()
    {
        InitializeComponent();
        // Drift-free maximize/restore (ported window-restore design).
        CrimsonAtomtic.Ui.Services.ManagedWindowRestore.Attach(this);
    }

    /// <summary>
    /// Copies the value stashed in <c>Tag</c> on the clicked button to
    /// the system clipboard. Mirror of the per-row copy handler used
    /// by Browse Items / Browse Characters — kept per-window so each
    /// dialog stays self-contained.
    /// </summary>
    private async void OnCopyButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control source)
        {
            return;
        }
        if (source.Tag is not string text || string.IsNullOrEmpty(text))
        {
            return;
        }
        var topLevel = TopLevel.GetTopLevel(source);
        if (topLevel?.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    /// <summary>
    /// "Go" button: ask the picker VM to fire its GotoRequested
    /// event. The MainWindow code-behind subscribes when it opens
    /// this dialog and routes through
    /// <c>MainWindowViewModel.NavigateToInventoryItemAsync</c>. The
    /// dialog stays open so the user can jump to several items in
    /// sequence.
    /// </summary>
    private void OnGotoButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control source) return;
        if (source.DataContext is not FindItemsRow row) return;
        if (DataContext is FindItemsViewModel vm)
        {
            vm.RequestGoto(row);
        }
    }
}

using Avalonia.Controls;
// Same Avalonia 12 dance as LocalizationSearchWindow — SetTextAsync
// lives on ClipboardExtensions, not directly on IClipboard.
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class ItemPickerWindow : Window
{
    public ItemPickerWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Copies the value stashed in <c>Tag</c> on the clicked button to
    /// the system clipboard. Mirrors
    /// <c>LocalizationSearchWindow.OnCopyButtonClick</c> — kept as a
    /// per-window handler rather than a shared static so each window
    /// is self-contained and can swap its copy semantics
    /// independently if a future feature needs it. Best-effort: a
    /// null / empty Tag or a missing clipboard implementation is a
    /// silent no-op.
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
    /// "+ Bag" button: forwards the row's ItemKey to the picker VM's
    /// <see cref="ItemPickerViewModel.AddItemRequested"/> event. The
    /// main window subscribes when it opens the picker (see
    /// <c>MainWindow.OnBrowseItemsClick</c>) and routes the request
    /// to <c>MainWindowViewModel.AddItemToCurrentListAsync</c>.
    /// </summary>
    private void OnAddToBagClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control source)
        {
            return;
        }
        // Avalonia 12 binds the Button.Tag as object — read uint via
        // an explicit cast so a non-uint value (shouldn't happen given
        // the binding shape) silently no-ops.
        if (source.Tag is not uint itemKey)
        {
            return;
        }
        if (DataContext is ItemPickerViewModel vm)
        {
            vm.RequestAddItem(itemKey);
        }
    }
}

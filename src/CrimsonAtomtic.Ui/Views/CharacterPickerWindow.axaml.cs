using Avalonia.Controls;
// Same Avalonia 12 dance as ItemPickerWindow — SetTextAsync lives on
// ClipboardExtensions, not directly on IClipboard.
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class CharacterPickerWindow : Window
{
    public CharacterPickerWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Copies the value stashed in <c>Tag</c> on the clicked button to
    /// the system clipboard. Mirror of
    /// <c>ItemPickerWindow.OnCopyButtonClick</c> — per-window handler
    /// rather than a shared static so each window stays self-contained
    /// and can swap copy semantics independently if a future feature
    /// needs it. Best-effort: a null / empty Tag or a missing clipboard
    /// implementation is a silent no-op.
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
}

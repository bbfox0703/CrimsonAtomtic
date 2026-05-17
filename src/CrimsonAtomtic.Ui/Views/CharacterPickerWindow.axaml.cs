using Avalonia.Controls;
// Same Avalonia 12 dance as ItemPickerWindow — SetTextAsync lives on
// ClipboardExtensions, not directly on IClipboard.
using Avalonia.Input.Platform;
using Avalonia.Interactivity;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class CharacterPickerWindow : Window
{
    public CharacterPickerWindow()
    {
        InitializeComponent();
        // Pick-mode wiring: when the VM is set and reports
        // IsPickMode=true, subscribe to PickConfirmed and close the
        // window with the chosen CharacterKey as the dialog result.
        // Browse-only callers (DataContext.IsPickMode == false) get
        // no subscription so the window stays open until the user
        // closes it manually.
        DataContextChanged += (_, _) =>
        {
            if (DataContext is CharacterPickerViewModel vm && vm.IsPickMode)
            {
                vm.PickConfirmed += key => Close((uint?)key);
            }
        };
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

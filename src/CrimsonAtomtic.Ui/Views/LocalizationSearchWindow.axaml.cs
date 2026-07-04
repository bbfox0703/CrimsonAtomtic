using Avalonia.Controls;
// SetTextAsync moved to ClipboardExtensions in Avalonia 12 — the raw
// IClipboard interface only exposes the generic SetDataAsync now.
using Avalonia.Input.Platform;
using Avalonia.Interactivity;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class LocalizationSearchWindow : Window
{
    public LocalizationSearchWindow()
    {
        InitializeComponent();
        // Drift-free maximize/restore (ported window-restore design).
        CrimsonAtomtic.Ui.Services.ManagedWindowRestore.Attach(this);
    }

    /// <summary>
    /// Copies the value stashed in <c>Tag</c> on the clicked button to
    /// the system clipboard. The XAML wires each per-row K / V / V₂
    /// button to this handler with its target string bound into Tag,
    /// so the handler stays content-agnostic. Best-effort: a null /
    /// empty Tag, or an environment without a clipboard available
    /// (e.g. running headless), is treated as a no-op rather than
    /// surfacing as an unhandled exception.
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

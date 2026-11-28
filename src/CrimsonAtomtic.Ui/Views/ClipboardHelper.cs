using Avalonia.Controls;
using Avalonia.Input.Platform;

namespace CrimsonAtomtic.Ui.Views;

/// <summary>
/// The copy buttons' shared handler: copies the button's string
/// <c>Tag</c> to the clipboard. A clipboard the OS keeps busy or refuses
/// is not worth an error dialog, so a failure is reported through
/// <see cref="Services.AppErrors"/> only when it is not a clipboard
/// access problem; an empty tag or a missing clipboard does nothing.
/// </summary>
internal static class ClipboardHelper
{
    public static async Task CopyTagAsync(object? sender)
    {
        if (sender is not Control { Tag: string text } source || string.IsNullOrEmpty(text))
        {
            return;
        }
        if (TopLevel.GetTopLevel(source)?.Clipboard is not { } clipboard)
        {
            return;
        }
        try
        {
            await clipboard.SetTextAsync(text);
        }
        catch (Exception ex) when (ex is System.Runtime.InteropServices.COMException
                                       or UnauthorizedAccessException or TimeoutException)
        {
            // The clipboard is held by another process; the user can click again.
        }
    }
}

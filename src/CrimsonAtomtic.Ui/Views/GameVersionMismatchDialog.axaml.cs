using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CrimsonAtomtic.RustInterop;

namespace CrimsonAtomtic.Ui.Views;

/// <summary>
/// Warning dialog shown when the loaded install's <c>meta/0.paver</c>
/// minor doesn't match <see cref="GameDataVersion.ParserTargetMinor"/>.
/// The parser targets a specific game-data schema; running it against
/// a different patch's data may parse-crash or silently mis-decode
/// (typical symptom: <c>iteminfo.pabgb</c> deserialization throws).
///
/// <para>
/// Returns <c>true</c> via <c>Close(true)</c> when the user clicks
/// "Continue anyway"; <c>false</c> via <c>Close(false)</c> on "Quit".
/// The hosting <see cref="App"/> shuts down the application on quit
/// — the same pattern <see cref="DisclaimerDialog"/> uses.
/// </para>
/// </summary>
public sealed partial class GameVersionMismatchDialog : Window
{
    public GameVersionMismatchDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show the warning over <paramref name="owner"/> if (and only if)
    /// <paramref name="detected"/> is non-null AND
    /// <see cref="GameDataVersion.IsCompatibleWithParser"/> is
    /// <c>false</c>. When the detected version is null (no install
    /// found, or paver read failed) we skip the dialog — the bootstrap
    /// would have already failed loudly elsewhere if that mattered.
    /// </summary>
    /// <returns>
    /// <c>true</c> when the dialog isn't shown (no mismatch) or the
    /// user clicks Continue; <c>false</c> when the user clicks Quit.
    /// Caller is expected to <c>desktop.Shutdown()</c> on a
    /// <c>false</c> return.
    /// </returns>
    public static async Task<bool> ShowIfMismatchedAsync(
        Window owner, GameDataVersion? detected)
    {
        if (detected is null || detected.Value.IsCompatibleWithParser)
        {
            return true;
        }
        var dlg = new GameVersionMismatchDialog();
        // Populate the version readouts. Plain text-block assignments
        // keep the dialog free of DataContext plumbing — there's only
        // one consumer and the data is captured at construction time.
        dlg.DetectedVersionText.Text = detected.Value.DisplayString;
        dlg.TargetVersionText.Text =
            $"1.{GameDataVersion.ParserTargetMinor:D2}.xx";
        var result = await dlg.ShowDialog<bool?>(owner);
        // OS-chrome close (X / Alt-F4) returns null — treat as Quit
        // so we don't silently swallow consent the user didn't give.
        return result == true;
    }

    private void OnContinue(object? sender, RoutedEventArgs e) => Close(true);
    private void OnQuit(object? sender, RoutedEventArgs e) => Close(false);
}

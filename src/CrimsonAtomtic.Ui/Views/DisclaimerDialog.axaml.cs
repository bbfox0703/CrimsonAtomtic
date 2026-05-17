using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CrimsonAtomtic.Ui.Services;

namespace CrimsonAtomtic.Ui.Views;

/// <summary>
/// First-launch legal disclaimer dialog. Shown once per machine — the
/// user's acceptance is persisted to
/// <see cref="AppSettings.DisclaimerAcceptedVersion"/> so subsequent
/// launches skip the prompt. Bumping <see cref="CurrentVersion"/> is
/// the supported channel for re-prompting every existing user after
/// the legal text materially changes (e.g. v1 → v2).
///
/// <para>
/// Returns <c>true</c> via <c>Close(true)</c> when the user clicks
/// "我同意 / I Accept"; <c>false</c> via <c>Close(false)</c> on
/// "離開 / Quit". The app shuts down when the user declines —
/// <see cref="App"/> wires that in <c>OnFrameworkInitializationCompleted</c>.
/// </para>
/// </summary>
public sealed partial class DisclaimerDialog : Window
{
    /// <summary>
    /// Current legal-disclaimer version. Increment whenever the
    /// <c>DisclaimerBody</c> resource changes materially — every user
    /// whose persisted <see cref="AppSettings.DisclaimerAcceptedVersion"/>
    /// is strictly less than this gets re-prompted on next launch.
    /// </summary>
    public const int CurrentVersion = 1;

    public DisclaimerDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show the disclaimer modally over <paramref name="owner"/>
    /// unless the user has already accepted the current version.
    /// Returns <c>true</c> when the user accepts (now or previously);
    /// <c>false</c> when they decline this launch. On a fresh accept,
    /// persists the new version to settings before returning.
    /// </summary>
    /// <remarks>
    /// Best-effort: a failure to persist (read-only %LOCALAPPDATA%,
    /// disk full, …) doesn't block startup — the user still gets in,
    /// they just see the dialog again next launch.
    /// </remarks>
    public static async Task<bool> ShowIfNotAcceptedAsync(
        Window owner, AppSettings settings, string localAppDataDirectory)
    {
        if (settings.DisclaimerAcceptedVersion is { } v && v >= CurrentVersion)
        {
            return true;
        }
        var dlg = new DisclaimerDialog();
        // ShowDialog<bool?> returns null when the user closes the
        // window via the OS chrome (X / Alt-F4) — treat that the same
        // as "Decline" so we don't accidentally infer consent from a
        // dismissed window.
        var result = await dlg.ShowDialog<bool?>(owner);
        var accepted = result == true;
        if (accepted)
        {
            AppSettingsStore.TrySave(
                localAppDataDirectory,
                settings with { DisclaimerAcceptedVersion = CurrentVersion });
        }
        return accepted;
    }

    private void OnAccept(object? sender, RoutedEventArgs e) => Close(true);
    private void OnDecline(object? sender, RoutedEventArgs e) => Close(false);
}

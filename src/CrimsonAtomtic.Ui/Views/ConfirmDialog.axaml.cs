using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CrimsonAtomtic.Ui.Views;

/// <summary>
/// Minimal Yes/No confirmation modal. Avalonia 12 doesn't ship a
/// MessageBox primitive, and pulling in a third-party dialog NuGet
/// just for a handful of confirm prompts isn't worth the trim-safety
/// surface area. ~30 lines of code-behind covers it.
///
/// Use via <see cref="ShowAsync"/>: it sets the message, opens
/// modally over <paramref name="owner"/>, and resolves to
/// <c>true</c> when the user picks Yes / <c>false</c> for No (or
/// closes the window any other way — close = No).
/// </summary>
public sealed partial class ConfirmDialog : Window
{
    public ConfirmDialog()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show a modal Yes/No dialog over <paramref name="owner"/>.
    /// Returns <c>true</c> for Yes, <c>false</c> for No (or any other
    /// close path). The dialog is sized for short messages — long
    /// prose wraps but the window doesn't auto-grow.
    /// </summary>
    public static Task<bool> ShowAsync(Window owner, string title, string message)
    {
        var dlg = new ConfirmDialog { Title = title };
        dlg.MessageText.Text = message;
        return dlg.ShowDialog<bool>(owner);
    }

    private void OnYesClick(object? sender, RoutedEventArgs e) => Close(true);
    private void OnNoClick(object? sender, RoutedEventArgs e)  => Close(false);
}

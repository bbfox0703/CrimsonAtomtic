using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

/// <summary>
/// Modal palette picker dialog. Caller passes the resolved color group
/// catalog + current RGB; dialog closes with the chosen
/// <c>(R, G, B)</c> or null on cancel.
/// </summary>
/// <remarks>
/// The VM raises <see cref="DyePalettePickerViewModel.CloseRequested"/>
/// the instant a cell is clicked — there's no separate OK button.
/// Cancel button + window close both resolve to null.
/// </remarks>
public sealed partial class DyePalettePickerWindow : Window
{
    public DyePalettePickerWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Show the picker modally over <paramref name="owner"/>. Returns
    /// the chosen <c>(R, G, B)</c> on cell-click, null on cancel /
    /// window close. Alpha is implicit 0xFF — every palette position
    /// uses opaque alpha per vendor docs.
    /// </summary>
    public static async Task<(byte R, byte G, byte B)?> ShowAsync(
        Window owner, DyePalettePickerViewModel vm)
    {
        var dlg = new DyePalettePickerWindow { DataContext = vm };
        vm.CloseRequested += () => dlg.Close();
        await dlg.ShowDialog(owner);
        return vm.HasResult ? (vm.ResultR, vm.ResultG, vm.ResultB) : null;
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close();
}

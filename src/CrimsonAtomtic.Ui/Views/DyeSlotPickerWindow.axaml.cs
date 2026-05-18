using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

/// <summary>
/// Modal slot picker for the Dye editor's "+ Add" action. Opens with a
/// list of slots <c>0..N-1</c> (N = the item's
/// <c>partprefabdyeslotinfo</c> slot count) labelled with each slot's
/// default material name. Returns the picked
/// <see cref="DyeSlotPickerOption.SlotIndex"/>, or <c>null</c> when the
/// user cancels.
/// </summary>
public sealed partial class DyeSlotPickerWindow : Window
{
    public DyeSlotPickerWindow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Open the modal over <paramref name="owner"/> and return the
    /// user-picked slot index (or <c>null</c> on cancel / close).
    /// </summary>
    public static async Task<int?> ShowAsync(
        Window owner, DyeSlotPickerViewModel vm)
    {
        var dlg = new DyeSlotPickerWindow { DataContext = vm };
        var result = await dlg.ShowDialog<int?>(owner);
        return result;
    }

    private void OnOkClick(object? sender, RoutedEventArgs e)
    {
        if (DataContext is DyeSlotPickerViewModel vm
            && vm.SelectedOption is { SlotIndex: var idx })
        {
            Close((int?)idx);
            return;
        }
        Close((int?)null);
    }

    private void OnCancelClick(object? sender, RoutedEventArgs e) => Close((int?)null);
}

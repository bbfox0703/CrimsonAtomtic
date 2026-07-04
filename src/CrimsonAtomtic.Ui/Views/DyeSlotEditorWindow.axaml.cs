using Avalonia.Controls;
using Avalonia.Interactivity;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class DyeSlotEditorWindow : Window
{
    public DyeSlotEditorWindow()
    {
        InitializeComponent();
        // Drift-free maximize/restore (ported window-restore design).
        CrimsonAtomtic.Ui.Services.ManagedWindowRestore.Attach(this);
        DataContextChanged += OnDataContextChanged;
    }

    /// <summary>
    /// Subscribe to <see cref="DyeSlotEditorViewModel.PickColorRequested"/>
    /// when the VM lands so per-row "Pick…" button clicks open the modal
    /// palette picker. Unhooked automatically when the window closes.
    /// </summary>
    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (DataContext is DyeSlotEditorViewModel vm)
        {
            vm.PickColorRequested += OnPickColorRequested;
        }
    }

    private async void OnPickColorRequested(DyeSlotRow row)
    {
        if (DataContext is not DyeSlotEditorViewModel vm) return;
        var catalog = vm.DyeColorGroupCatalog;
        if (catalog is null) return;
        if (row.SelectedColorGroup is not { } cg)
        {
            // Picker needs a theme — gracefully bail if the row doesn't
            // have one selected (e.g. brand-new dye element with the
            // color-group field still absent). Real UX should disable
            // the Pick button in this state; for now the click is a no-op.
            return;
        }
        var pickerVm = new DyePalettePickerViewModel(
            catalog,
            cg.Key,
            cg.Display,
            row.R, row.G, row.B,
            row.SlotLabel);
        var rgb = await DyePalettePickerWindow.ShowAsync(this, pickerVm);
        if (rgb is { } v)
        {
            row.ApplyPickedColor(v.R, v.G, v.B);
        }
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

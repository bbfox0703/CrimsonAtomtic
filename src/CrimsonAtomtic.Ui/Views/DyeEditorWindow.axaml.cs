using Avalonia.Controls;
using Avalonia.Interactivity;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class DyeEditorWindow : Window
{
    public DyeEditorWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// Per-row "Edit…" button: forwards the row to the VM's
    /// <see cref="DyeEditorViewModel.RequestEdit"/>. The MainWindow
    /// code-behind subscribes to <see cref="DyeEditorViewModel.EditRequested"/>
    /// and opens the per-item slot editor child window.
    /// </summary>
    private void OnEditButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control source) return;
        if (source.DataContext is not DyeEditorItemRow row) return;
        if (DataContext is DyeEditorViewModel vm)
        {
            vm.RequestEdit(row);
        }
    }
}

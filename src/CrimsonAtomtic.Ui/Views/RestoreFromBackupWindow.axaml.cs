using Avalonia.Controls;
using Avalonia.Interactivity;
using CrimsonAtomtic.Ui.Services;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class RestoreFromBackupWindow : Window
{
    public RestoreFromBackupWindow()
    {
        InitializeComponent();
        // Drift-free maximize/restore (ported window-restore design).
        CrimsonAtomtic.Ui.Services.ManagedWindowRestore.Attach(this);
    }

    /// <summary>
    /// Forwards the row's <see cref="BackupEntry"/> to the picker VM's
    /// <see cref="RestoreFromBackupViewModel.RestoreRequested"/> event.
    /// MainWindow subscribes when opening the dialog and routes the
    /// request to <c>MainWindowViewModel.RestoreFromBackupAsync</c>.
    /// </summary>
    private void OnRestoreButtonClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not Control source)
        {
            return;
        }
        if (source.Tag is not BackupEntry entry)
        {
            return;
        }
        if (DataContext is RestoreFromBackupViewModel vm)
        {
            vm.RequestRestore(entry);
            // The actual restore happens asynchronously on the main
            // window; close the picker so the user sees the result via
            // the status footer instead of staring at the dialog.
            Close();
        }
    }
}

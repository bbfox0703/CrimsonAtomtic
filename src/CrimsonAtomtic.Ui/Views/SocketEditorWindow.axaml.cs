using Avalonia.Controls;
using Avalonia.Interactivity;
using CrimsonAtomtic.Ui.ViewModels;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class SocketEditorWindow : Window
{
    public SocketEditorWindow()
    {
        InitializeComponent();
        // Drift-free maximize/restore (ported window-restore design).
        CrimsonAtomtic.Ui.Services.ManagedWindowRestore.Attach(this);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// "Edit custom sets…" toolbar button. Walks up to the MainWindow
    /// to grab the platform-paths singleton (needed for AppSettings IO),
    /// opens the dialog modal, and on save shows a status hint that
    /// the open Sockets editor won't auto-reflect the new sets until
    /// reopen.
    /// </summary>
    private async void OnEditCustomGemSetsClick(object? sender, RoutedEventArgs e)
    {
        if (Owner is not MainWindow mainWindow
            || mainWindow.DataContext is not MainWindowViewModel mainVm)
        {
            return;
        }
        var existing = mainVm.LoadCustomGemSets();
        var editorVm = new CustomGemSetsEditorViewModel(mainVm.GetPlatformPaths(), existing);
        var dlg = new CustomGemSetsEditorWindow { DataContext = editorVm };
        await dlg.ShowDialog(this);
        if (editorVm.Saved && DataContext is SocketEditorViewModel socketsVm)
        {
            // Reload the persisted sets + refresh the Apply-Set dropdown
            // so the just-saved sets show up without reopening the
            // Sockets editor window.
            socketsVm.RefreshCustomGemSets(editorVm.GetCurrentlyPersistedSets());
        }
    }
}

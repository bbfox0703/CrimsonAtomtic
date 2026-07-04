using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CrimsonAtomtic.Ui.Views;

/// <summary>
/// Code-behind for the Tools → Browse Character References dialog.
/// All meaningful logic lives in
/// <see cref="CrimsonAtomtic.Ui.ViewModels.CharacterRefsBrowserViewModel"/>;
/// this is just window plumbing. The Jump-button per-row event is
/// routed via the VM's <c>JumpToBlockRequested</c> event, which the
/// MainWindow code-behind subscribes to.
/// </summary>
public sealed partial class CharacterRefsBrowserWindow : Window
{
    public CharacterRefsBrowserWindow()
    {
        InitializeComponent();
        // Drift-free maximize/restore (ported window-restore design).
        CrimsonAtomtic.Ui.Services.ManagedWindowRestore.Attach(this);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

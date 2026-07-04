using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class KnowledgeEditorWindow : Window
{
    public KnowledgeEditorWindow()
    {
        InitializeComponent();
        // Drift-free maximize/restore (ported window-restore design).
        CrimsonAtomtic.Ui.Services.ManagedWindowRestore.Attach(this);
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

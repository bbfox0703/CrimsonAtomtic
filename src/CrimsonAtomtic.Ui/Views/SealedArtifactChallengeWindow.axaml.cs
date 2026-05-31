using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class SealedArtifactChallengeWindow : Window
{
    public SealedArtifactChallengeWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

using Avalonia.Controls;
using Avalonia.Interactivity;

namespace CrimsonAtomtic.Ui.Views;

public sealed partial class AbyssGatesWindow : Window
{
    public AbyssGatesWindow()
    {
        InitializeComponent();
    }

    private void OnCloseClick(object? sender, RoutedEventArgs e) => Close();
}

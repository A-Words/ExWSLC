using System.Windows;
using System.Windows.Controls;
using ExWSLC.ViewModels;
using Wpf.Ui.Controls;

namespace ExWSLC;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        Loaded += async (_, _) => await _viewModel.InitializeAsync();
        Closed += (_, _) => _viewModel.Dispose();
    }

    private void NavigationButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.Button { Tag: string value } && int.TryParse(value, out var index))
            _viewModel.SelectedPageIndex = index;
    }

    private void RegistryPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is System.Windows.Controls.PasswordBox passwordBox) _viewModel.RegistryPassword = passwordBox.Password;
    }
}

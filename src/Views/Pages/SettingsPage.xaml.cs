using System.Windows;
using System.Windows.Controls;
using ExWSLC.ViewModels;
using ExWSLC.Views;

namespace ExWSLC.Views.Pages;

public partial class SettingsPage : System.Windows.Controls.Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    public SettingsPage(SettingsPageViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void RegistryPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.PasswordBox passwordBox &&
            DataContext is SettingsPageViewModel viewModel)
        {
            viewModel.RegistryPassword = passwordBox.Password;
        }
    }
}

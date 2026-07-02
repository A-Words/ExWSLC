using System.Windows;
using System.Windows.Controls;

namespace ExWSLC.Views.Pages;

public partial class SettingsPage : System.Windows.Controls.Page
{
    public SettingsPage()
    {
        InitializeComponent();
        DataContext = App.Current.ViewModel;
    }

    private void RegistryPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.PasswordBox passwordBox)
            App.Current.ViewModel.RegistryPassword = passwordBox.Password;
    }
}

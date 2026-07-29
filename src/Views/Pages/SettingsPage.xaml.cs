using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using ExWSLC.ViewModels;
using ExWSLC.Views;

namespace ExWSLC.Views.Pages;

public partial class SettingsPage : System.Windows.Controls.Page
{
    public SettingsPage()
    {
        InitializeComponent();
    }

    public SettingsPage(SettingsViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void RegistryPasswordBox_OnPasswordChanged(object sender, RoutedEventArgs e)
    {
        if (sender is Wpf.Ui.Controls.PasswordBox passwordBox &&
            DataContext is SettingsViewModel viewModel)
        {
            viewModel.RegistryPassword = passwordBox.Password;
        }
    }

    private void SettingsScrollViewer_OnPreviewMouseWheel(object sender, MouseWheelEventArgs e)
    {
        if (sender is not ScrollViewer scrollViewer ||
            scrollViewer.ScrollableHeight <= 0 ||
            SystemParameters.WheelScrollLines == 0)
        {
            return;
        }

        var wheelDetents = Math.Max(1, Math.Abs(e.Delta) / Mouse.MouseWheelDeltaForOneLine);
        for (var detent = 0; detent < wheelDetents; detent++)
        {
            if (SystemParameters.WheelScrollLines == -1)
            {
                if (e.Delta > 0) scrollViewer.PageUp();
                else scrollViewer.PageDown();
                continue;
            }

            for (var line = 0; line < SystemParameters.WheelScrollLines; line++)
            {
                if (e.Delta > 0) scrollViewer.LineUp();
                else scrollViewer.LineDown();
            }
        }

        e.Handled = true;
    }
}

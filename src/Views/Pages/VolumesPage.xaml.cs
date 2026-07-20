using System.Windows;
using System.Windows.Controls;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using ExWSLC.Views.Dialogs;
using Wpf.Ui.Controls;

namespace ExWSLC.Views.Pages;

public partial class VolumesPage : Page
{
    public VolumesPage()
    {
        InitializeComponent();
    }

    public VolumesPage(VolumesViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private async void CreateVolumeButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not VolumesViewModel viewModel || Window.GetWindow(this) is not { } window) return;
        var host = ContentDialogHost.GetForWindow(window);
        if (host is null) return;

        var dialog = new ContentDialog(host)
        {
            Title = LocalizationService.GetString("CreateVolume", "Create volume"),
            Content = new VolumeCreateDialogContent { DataContext = viewModel },
            PrimaryButtonText = LocalizationService.GetString("Create", "Create"),
            CloseButtonText = LocalizationService.GetString("Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync(CancellationToken.None) == ContentDialogResult.Primary)
            await viewModel.CreateVolumeCommand.ExecuteAsync(null);
    }

    private void OpenContextMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button { ContextMenu: { } menu } button) return;
        e.Handled = true;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }
}

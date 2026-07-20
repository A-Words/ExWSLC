using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using ExWSLC.Views.Dialogs;
using Wpf.Ui.Controls;

namespace ExWSLC.Views.Pages;

public partial class NetworksPage : Page
{
    public NetworksPage()
    {
        InitializeComponent();
    }

    public NetworksPage(NetworksViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private async void CreateNetworkButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not NetworksViewModel viewModel || Window.GetWindow(this) is not { } window) return;
        var host = ContentDialogHost.GetForWindow(window);
        if (host is null) return;

        var dialog = new ContentDialog(host)
        {
            Title = LocalizationService.GetString("CreateNetwork", "Create network"),
            Content = new NetworkCreateDialogContent { DataContext = viewModel },
            PrimaryButtonText = LocalizationService.GetString("Create", "Create"),
            CloseButtonText = LocalizationService.GetString("Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary,
            IsPrimaryButtonEnabled = viewModel.CreateNetworkCommand.CanExecute(null)
        };

        PropertyChangedEventHandler updateCanCreate = (_, args) =>
        {
            if (args.PropertyName == nameof(NetworksViewModel.NetworkName))
                dialog.IsPrimaryButtonEnabled = viewModel.CreateNetworkCommand.CanExecute(null);
        };
        viewModel.PropertyChanged += updateCanCreate;
        try
        {
            if (await dialog.ShowAsync(CancellationToken.None) == ContentDialogResult.Primary)
                await viewModel.CreateNetworkCommand.ExecuteAsync(null);
        }
        finally
        {
            viewModel.PropertyChanged -= updateCanCreate;
        }
    }

    private void OpenContextMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button { ContextMenu: { } menu } button) return;
        e.Handled = true;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }
}

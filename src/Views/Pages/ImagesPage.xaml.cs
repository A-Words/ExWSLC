using System.Windows;
using System.Windows.Controls;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using ExWSLC.Views.Dialogs;
using Wpf.Ui.Controls;

namespace ExWSLC.Views.Pages;

public partial class ImagesPage : Page
{
    public ImagesPage()
    {
        InitializeComponent();
    }

    public ImagesPage(ImagesViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private async void PullImageButton_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is not ImagesViewModel viewModel || Window.GetWindow(this) is not { } window) return;
        var host = ContentDialogHost.GetForWindow(window);
        if (host is null) return;

        var dialog = new ContentDialog(host)
        {
            Title = LocalizationService.GetString("Pull", "Pull image"),
            Content = new ImagePullDialogContent { DataContext = viewModel },
            PrimaryButtonText = LocalizationService.GetString("Pull", "Pull"),
            CloseButtonText = LocalizationService.GetString("Cancel", "Cancel"),
            DefaultButton = ContentDialogButton.Primary
        };
        if (await dialog.ShowAsync(CancellationToken.None) == ContentDialogResult.Primary)
            await viewModel.PullImageCommand.ExecuteAsync(null);
    }

    private void ShowImageOperationsMenuItem_Click(object sender, RoutedEventArgs e)
    {
        ImageOperationsExpander.IsExpanded = true;
        ImageOperationsExpander.BringIntoView();
    }

    private void OpenContextMenuButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button { ContextMenu: { } menu } button) return;
        e.Handled = true;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }
}

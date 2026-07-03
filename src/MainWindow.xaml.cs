using System.Windows;
using ExWSLC.ViewModels;
using ExWSLC.Views.Pages;
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
        Loaded += OnLoaded;
        Closed += (_, _) => _viewModel.Dispose();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Navigate(typeof(SettingsPage));
        RootNavigation.Navigate(typeof(TasksPage));
        RootNavigation.Navigate(typeof(ResourcesPage));
        RootNavigation.Navigate(typeof(ImagesPage));
        RootNavigation.Navigate(typeof(ContainersPage));
        RootNavigation.Navigate(typeof(OverviewPage));
        await _viewModel.InitializeAsync();
    }

    public void Navigate(Type pageType) => RootNavigation.Navigate(pageType);
}

using System.Windows;
using ExWSLC.Services;
using ExWSLC.ViewModels;
using ExWSLC.Views;
using ExWSLC.Views.Pages.Containers;
using Wpf.Ui.Controls;

namespace ExWSLC;

public partial class MainWindow : FluentWindow
{
    private readonly MainViewModel _viewModel;
    private readonly SystemThemeWatcher _themeWatcher = new();

    public MainWindow(MainViewModel viewModel)
    {
        _viewModel = viewModel;
        DataContext = viewModel;
        InitializeComponent();
        RootNavigation.SetPageProviderService(new AppPageProvider(viewModel));
        SourceInitialized += OnSourceInitialized;
        Loaded += OnLoaded;
        Closed += OnClosed;
        _viewModel.ApplyConfiguredTheme();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _themeWatcher.Attach(this);
        _themeWatcher.ThemeChanged += (_, _) => _viewModel.RefreshSystemTheme();
    }

    private async void OnLoaded(object sender, RoutedEventArgs e)
    {
        RootNavigation.Navigate(typeof(ContainersPage));
        await _viewModel.InitializeAsync();
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _themeWatcher.Dispose();
        _viewModel.Dispose();
    }

    public void Navigate(Type pageType) => RootNavigation.Navigate(pageType);
}

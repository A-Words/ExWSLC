using ExWSLC.ViewModels;
using ExWSLC.Views.Pages;
using ExWSLC.Views.Pages.Containers;
using Wpf.Ui.Abstractions;

namespace ExWSLC.Views;

internal sealed class AppPageProvider : INavigationViewPageProvider
{
    private readonly Dictionary<Type, Func<object>> _pageFactories;

    public AppPageProvider(MainViewModel viewModel)
    {
        _pageFactories = new Dictionary<Type, Func<object>>
        {
            [typeof(OverviewPage)] = () => new OverviewPage(viewModel.OverviewPage),
            [typeof(ContainersPage)] = () => new ContainersPage(viewModel.Containers),
            [typeof(ImagesPage)] = () => new ImagesPage(viewModel.ImagesPage),
            [typeof(NetworksPage)] = () => new NetworksPage(viewModel.NetworksPage),
            [typeof(VolumesPage)] = () => new VolumesPage(viewModel.VolumesPage),
            [typeof(TasksPage)] = () => new TasksPage(viewModel.TasksPage),
            [typeof(SettingsPage)] = () => new SettingsPage(viewModel.SettingsPage),
        };
    }

    public object? GetPage(Type pageType) =>
        _pageFactories.TryGetValue(pageType, out var factory) ? factory() : null;
}

using System.Windows.Controls;
using ExWSLC.ViewModels;
using ExWSLC.Views.Pages;
using ExWSLC.Views.Pages.Containers;
using Wpf.Ui.Abstractions;

namespace ExWSLC.Views;

internal sealed class AppPageProvider(MainViewModel viewModel) : INavigationViewPageProvider
{
    public object? GetPage(Type pageType)
    {
        if (pageType == typeof(OverviewPage)) return new OverviewPage(viewModel.OverviewPage);
        if (pageType == typeof(ContainersPage)) return new ContainersPage(viewModel.Containers);
        if (pageType == typeof(ImagesPage)) return new ImagesPage(viewModel.ImagesPage);
        if (pageType == typeof(ResourcesPage)) return new ResourcesPage(viewModel.ResourcesPage);
        if (pageType == typeof(TasksPage)) return new TasksPage(viewModel.TasksPage);
        if (pageType == typeof(SettingsPage)) return new SettingsPage(viewModel.SettingsPage);

        return Activator.CreateInstance(pageType) as Page;
    }
}

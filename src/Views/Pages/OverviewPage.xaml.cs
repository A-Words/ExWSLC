using System.Windows;
using System.Windows.Controls;
using ExWSLC.Models;
using ExWSLC.ViewModels;
using ExWSLC.Views;
using ExWSLC.Views.Pages.Containers;

namespace ExWSLC.Views.Pages;

public partial class OverviewPage : Page
{
    public OverviewPage()
    {
        InitializeComponent();
    }

    public OverviewPage(OverviewPageViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }

    private void OpenContainers_Click(object sender, RoutedEventArgs e) => ((MainWindow)App.Current.MainWindow).Navigate(typeof(ContainersPage));
    private void OpenImages_Click(object sender, RoutedEventArgs e) => ((MainWindow)App.Current.MainWindow).Navigate(typeof(ImagesPage));
    private void OpenResources_Click(object sender, RoutedEventArgs e) => ((MainWindow)App.Current.MainWindow).Navigate(typeof(ResourcesPage));
    private void OpenTasks_Click(object sender, RoutedEventArgs e) => ((MainWindow)App.Current.MainWindow).Navigate(typeof(TasksPage));

    private void ConfigureContainer_Click(object sender, RoutedEventArgs e)
    {
        if (DataContext is OverviewPageViewModel viewModel)
        {
            viewModel.ShowCreateContainer();
        }

        ((MainWindow)App.Current.MainWindow).Navigate(typeof(ContainersPage));
    }

    private void OpenContainer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ContainerSummary container } &&
            DataContext is OverviewPageViewModel viewModel)
        {
            viewModel.SelectContainer(container);
            ((MainWindow)App.Current.MainWindow).Navigate(typeof(ContainersPage));
        }
    }
}

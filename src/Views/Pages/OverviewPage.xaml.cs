using System.Windows;
using System.Windows.Controls;
using ExWSLC.Models;
using ExWSLC.Views.Pages.Containers;

namespace ExWSLC.Views.Pages;

public partial class OverviewPage : Page
{
    public OverviewPage()
    {
        InitializeComponent();
        DataContext = App.Current.ViewModel;
    }

    private void OpenContainers_Click(object sender, RoutedEventArgs e) => ((MainWindow)App.Current.MainWindow).Navigate(typeof(ContainersPage));
    private void OpenImages_Click(object sender, RoutedEventArgs e) => ((MainWindow)App.Current.MainWindow).Navigate(typeof(ImagesPage));
    private void OpenResources_Click(object sender, RoutedEventArgs e) => ((MainWindow)App.Current.MainWindow).Navigate(typeof(ResourcesPage));
    private void OpenTasks_Click(object sender, RoutedEventArgs e) => ((MainWindow)App.Current.MainWindow).Navigate(typeof(TasksPage));

    private void ConfigureContainer_Click(object sender, RoutedEventArgs e)
    {
        App.Current.ViewModel.ShowCreateContainerCommand.Execute(null);
        ((MainWindow)App.Current.MainWindow).Navigate(typeof(ContainersPage));
    }

    private void OpenContainer_Click(object sender, RoutedEventArgs e)
    {
        if (sender is FrameworkElement { DataContext: ContainerSummary container })
        {
            App.Current.ViewModel.SelectedContainer = container;
            ((MainWindow)App.Current.MainWindow).Navigate(typeof(ContainersPage));
        }
    }
}

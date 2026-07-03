using System.Windows;
using System.Windows.Controls;

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
}

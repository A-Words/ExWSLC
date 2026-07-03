using System.Windows.Controls;

namespace ExWSLC.Views.Pages;

public partial class ResourcesPage : Page
{
    public ResourcesPage()
    {
        InitializeComponent();
        DataContext = App.Current.ViewModel;
    }
}

using System.Windows.Controls;

namespace ExWSLC.Views.Pages.Containers;

public partial class ContainersPage : Page
{
    public ContainersPage()
    {
        InitializeComponent();
        DataContext = App.Current.ViewModel;
    }
}

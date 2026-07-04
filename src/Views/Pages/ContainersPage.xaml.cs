using System.Windows.Controls;

namespace ExWSLC.Views.Pages;

public partial class ContainersPage : Page
{
    public ContainersPage()
    {
        InitializeComponent();
        DataContext = App.Current.ViewModel;
    }
}

using System.Windows.Controls;

namespace ExWSLC.Views.Pages.Containers;

public partial class ContainerListView : UserControl
{
    public ContainerListView()
    {
        InitializeComponent();
        DataContext = App.Current.ViewModel;
    }
}

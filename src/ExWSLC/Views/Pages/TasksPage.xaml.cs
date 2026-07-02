using System.Windows.Controls;

namespace ExWSLC.Views.Pages;

public partial class TasksPage : Page
{
    public TasksPage()
    {
        InitializeComponent();
        DataContext = App.Current.ViewModel;
    }
}

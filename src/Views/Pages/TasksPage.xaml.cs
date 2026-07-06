using System.Windows.Controls;
using ExWSLC.ViewModels;
using ExWSLC.Views;

namespace ExWSLC.Views.Pages;

public partial class TasksPage : Page
{
    public TasksPage()
    {
        InitializeComponent();
    }

    public TasksPage(TasksViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}

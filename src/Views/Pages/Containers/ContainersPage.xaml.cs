using System.Windows.Controls;
using ExWSLC.ViewModels;
using ExWSLC.Views;

namespace ExWSLC.Views.Pages.Containers;

public partial class ContainersPage : Page
{
    public ContainersPage()
    {
        InitializeComponent();
    }

    public ContainersPage(ContainersViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}

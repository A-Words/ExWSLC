using System.Windows.Controls;
using ExWSLC.ViewModels;
using ExWSLC.Views;

namespace ExWSLC.Views.Pages;

public partial class ResourcesPage : Page
{
    public ResourcesPage()
    {
        InitializeComponent();
    }

    public ResourcesPage(ResourcesViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}

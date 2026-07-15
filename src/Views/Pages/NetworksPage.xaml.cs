using System.Windows.Controls;
using ExWSLC.ViewModels;

namespace ExWSLC.Views.Pages;

public partial class NetworksPage : Page
{
    public NetworksPage()
    {
        InitializeComponent();
    }

    public NetworksPage(NetworksViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}

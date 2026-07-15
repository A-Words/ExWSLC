using System.Windows.Controls;
using ExWSLC.ViewModels;

namespace ExWSLC.Views.Pages;

public partial class VolumesPage : Page
{
    public VolumesPage()
    {
        InitializeComponent();
    }

    public VolumesPage(VolumesViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}

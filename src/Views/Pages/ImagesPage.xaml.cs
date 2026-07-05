using System.Windows.Controls;
using ExWSLC.ViewModels;
using ExWSLC.Views;

namespace ExWSLC.Views.Pages;

public partial class ImagesPage : Page
{
    public ImagesPage()
    {
        InitializeComponent();
    }

    public ImagesPage(ImagesPageViewModel viewModel) : this()
    {
        DataContext = viewModel;
    }
}

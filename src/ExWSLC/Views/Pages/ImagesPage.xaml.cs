using System.Windows.Controls;

namespace ExWSLC.Views.Pages;

public partial class ImagesPage : Page
{
    public ImagesPage()
    {
        InitializeComponent();
        DataContext = App.Current.ViewModel;
    }
}

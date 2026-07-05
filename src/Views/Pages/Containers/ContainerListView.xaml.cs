using System.Windows.Controls;

namespace ExWSLC.Views.Pages.Containers;

public partial class ContainerListView : UserControl
{
    public ContainerListView()
    {
        InitializeComponent();
    }

    private void MoreActionsButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button { ContextMenu: { } menu } button) return;
        e.Handled = true;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }
}

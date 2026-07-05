using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ExWSLC.Views.Pages.Containers;

public partial class ContainerDetailView : UserControl
{
    private const double DetailHeaderHorizontalPadding = 44;
    private const double DetailHeaderMinimumInfoWidth = 560;
    private const double DetailHeaderActionsSingleRowWidth = 440;
    private const double DetailHeaderWrappedActionsWidth = 260;
    private const double DetailHeaderStackedBreakpoint = 760;

    public ContainerDetailView()
    {
        InitializeComponent();
    }

    private void ContainerDetailHeader_SizeChanged(object sender, SizeChangedEventArgs eventArgs)
    {
        if (sender is not FrameworkElement header) return;

        var actions = FindDescendantByName<WrapPanel>(header, "DetailHeaderActions");
        if (actions is null) return;

        if (header.ActualWidth < DetailHeaderStackedBreakpoint)
        {
            Grid.SetRow(actions, 1);
            Grid.SetColumn(actions, 0);
            Grid.SetColumnSpan(actions, 2);
            actions.HorizontalAlignment = HorizontalAlignment.Left;
            actions.Margin = new Thickness(11, 16, 0, 0);
            actions.MaxWidth = Math.Max(240, header.ActualWidth - DetailHeaderHorizontalPadding);
            return;
        }

        Grid.SetRow(actions, 0);
        Grid.SetColumn(actions, 1);
        Grid.SetColumnSpan(actions, 1);
        actions.HorizontalAlignment = HorizontalAlignment.Right;
        actions.Margin = new Thickness(0);
        var availableWidth = Math.Max(0, header.ActualWidth - DetailHeaderHorizontalPadding);
        var singleRowFits = availableWidth >= DetailHeaderMinimumInfoWidth + DetailHeaderActionsSingleRowWidth;
        actions.MaxWidth = singleRowFits ? double.PositiveInfinity : DetailHeaderWrappedActionsWidth;
    }

    private static T? FindDescendantByName<T>(DependencyObject parent, string name)
        where T : FrameworkElement
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T element && element.Name == name) return element;

            var descendant = FindDescendantByName<T>(child, name);
            if (descendant is not null) return descendant;
        }

        return null;
    }
}

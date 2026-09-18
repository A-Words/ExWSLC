using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;

namespace ExWSLC.Views.Pages.Containers;

public partial class ContainerListView : UserControl
{
    public static readonly DependencyProperty ListLayoutProperty = DependencyProperty.Register(
        nameof(ListLayout), typeof(ContainerListLayout), typeof(ContainerListView),
        new PropertyMetadata(ContainerListLayout.FromViewport(1000)));

    public static readonly DependencyProperty HorizontalTranslationProperty = DependencyProperty.Register(
        nameof(HorizontalTranslation), typeof(double), typeof(ContainerListView), new PropertyMetadata(0d));

    public double HorizontalTranslation
    {
        get => (double)GetValue(HorizontalTranslationProperty);
        private set => SetValue(HorizontalTranslationProperty, value);
    }

    public ContainerListLayout ListLayout
    {
        get => (ContainerListLayout)GetValue(ListLayoutProperty);
        private set => SetValue(ListLayoutProperty, value);
    }

    public ContainerListView()
    {
        InitializeComponent();
    }

    private void ContainerScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (e.ViewportWidthChange == 0 || sender is not ScrollViewer { ViewportWidth: > 0 } viewer) return;
        ListLayout = ContainerListLayout.FromViewport(viewer.ViewportWidth);
    }

    private void ContainerHorizontalScrollBar_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        HorizontalTranslation = -e.NewValue;
    }

    private void ScrollableColumns_RequestBringIntoView(object sender, RequestBringIntoViewEventArgs e)
    {
        if (sender is not Grid columns || e.TargetObject is not UIElement target || target == columns) return;
        var bounds = target.TransformToAncestor(columns).TransformBounds(new Rect(target.RenderSize));
        var offset = ContainerHorizontalScrollBar.Value;
        if (bounds.Left < offset)
            ContainerHorizontalScrollBar.Value = Math.Max(0, bounds.Left);
        else if (bounds.Right > offset + ListLayout.DataViewport)
            ContainerHorizontalScrollBar.Value = Math.Min(ListLayout.HorizontalScrollRange, bounds.Right - ListLayout.DataViewport);
    }

    private void MoreActionsButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        if (sender is not Wpf.Ui.Controls.Button { ContextMenu: { } menu } button) return;
        e.Handled = true;
        menu.PlacementTarget = button;
        menu.IsOpen = true;
    }

    private void PortMappingsButton_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement { Tag: Popup popup }) return;
        e.Handled = true;
        popup.IsOpen = true;
    }
}

// Both the header and every row bind to these widths, including when a scrollbar appears.
public sealed record ContainerListLayout(
    double Name, double Image, double Ports, double Cpu, double Memory,
    double Actions, double DataViewport)
{
    public double DataWidth => Name + Image + Ports + Cpu + Memory;
    public double RowWidth => DataViewport + Actions;
    public double HorizontalScrollRange => Math.Max(0, DataWidth - DataViewport);
    public bool HasHorizontalOverflow => HorizontalScrollRange > .01;

    internal static ContainerListLayout FromViewport(double viewportWidth)
    {
        var compactColumns = viewportWidth < 1100;
        const double actions = 88;
        const double cpu = 86;
        const double memory = 106;
        var nameMinimum = compactColumns ? 160 : 180;
        var imageMinimum = compactColumns ? 132 : 160;
        var portsMinimum = compactColumns ? 144 : 180;
        var extra = Math.Max(0, viewportWidth - 28 - actions - cpu - memory -
            nameMinimum - imageMinimum - portsMinimum);
        return new(nameMinimum + extra * .34, imageMinimum + extra * .30,
            portsMinimum + extra * .36, cpu, memory, actions,
            Math.Max(0, viewportWidth - 28 - actions));
    }
}

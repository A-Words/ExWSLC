using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ExWSLC.Views.Pages;

public partial class ContainersPage : Page
{
    private double _containerListHorizontalOffset;
    private bool _isSyncingContainerListScroll;

    public ContainersPage()
    {
        InitializeComponent();
        DataContext = App.Current.ViewModel;
    }

    private void ContainerColumnsScrollViewer_OnScrollChanged(object sender, ScrollChangedEventArgs e)
    {
        if (_isSyncingContainerListScroll)
        {
            return;
        }

        if (sender is not ScrollViewer scrollViewer) return;

        _containerListHorizontalOffset = scrollViewer.HorizontalOffset;
        SyncContainerListScrollViewers();
    }

    private void RowColumnsScrollViewer_OnLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ScrollViewer scrollViewer)
        {
            scrollViewer.ScrollToHorizontalOffset(_containerListHorizontalOffset);
        }
    }

    private void SyncContainerListScrollViewers()
    {
        _isSyncingContainerListScroll = true;
        try
        {
            foreach (var scrollViewer in FindVisualChildren<ScrollViewer>(this))
            {
                if (scrollViewer.Name is "ContainerColumnsScrollViewer" or "ContainerColumnsScrollBarViewer" or "RowColumnsScrollViewer")
                {
                    scrollViewer.ScrollToHorizontalOffset(_containerListHorizontalOffset);
                }
            }
        }
        finally
        {
            _isSyncingContainerListScroll = false;
        }
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T match)
            {
                yield return match;
            }

            foreach (var descendant in FindVisualChildren<T>(child))
            {
                yield return descendant;
            }
        }
    }
}

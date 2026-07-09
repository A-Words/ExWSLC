using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ExWSLC.Behaviors;

/// <summary>
/// Keeps a <see cref="ListBox"/> pinned to its bottom while items stream in, as long as the user
/// is already viewing the bottom. Scrolling up to read history pauses auto-scroll until the user
/// returns to the bottom. Intended for streaming log viewers.
/// </summary>
public static class ListBoxAutoScrollBehavior
{
    public static readonly DependencyProperty IsEnabledProperty =
        DependencyProperty.RegisterAttached(
            "IsEnabled",
            typeof(bool),
            typeof(ListBoxAutoScrollBehavior),
            new PropertyMetadata(false, OnIsEnabledChanged));

    public static bool GetIsEnabled(DependencyObject element) => (bool)element.GetValue(IsEnabledProperty);

    public static void SetIsEnabled(DependencyObject element, bool value) => element.SetValue(IsEnabledProperty, value);

    private static readonly DependencyProperty ControllerProperty =
        DependencyProperty.RegisterAttached(
            "Controller",
            typeof(Controller),
            typeof(ListBoxAutoScrollBehavior),
            new PropertyMetadata(null));

    private static void OnIsEnabledChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not ListBox listBox) return;
        if (DesignerProperties.GetIsInDesignMode(listBox)) return; // no live scrolling in the designer
        var controller = (Controller?)listBox.GetValue(ControllerProperty);
        if ((bool)e.NewValue)
        {
            controller ??= new Controller(listBox);
            listBox.SetValue(ControllerProperty, controller);
            controller.Attach();
        }
        else
        {
            controller?.Detach();
            listBox.ClearValue(ControllerProperty);
        }
    }

    private sealed class Controller
    {
        private readonly ListBox _listBox;
        private ScrollViewer? _scrollViewer;
        private bool _stickToBottom = true;
        private bool _hooked;

        public Controller(ListBox listBox) => _listBox = listBox;

        public void Attach()
        {
            _listBox.Loaded += OnLoaded;
            _listBox.Unloaded += OnUnloaded;
            if (_listBox.IsLoaded) Hook();
        }

        public void Detach()
        {
            _listBox.Loaded -= OnLoaded;
            _listBox.Unloaded -= OnUnloaded;
            Unhook();
        }

        private void OnLoaded(object? sender, RoutedEventArgs e) => Hook();

        private void OnUnloaded(object? sender, RoutedEventArgs e) => Unhook();

        private void Hook()
        {
            if (_hooked) return;
            _scrollViewer = FindVisualChild<ScrollViewer>(_listBox);
            if (_scrollViewer is null) return;
            _scrollViewer.ScrollChanged += OnScrollChanged;
            _hooked = true;
            if (_stickToBottom) _scrollViewer.ScrollToBottom();
        }

        private void Unhook()
        {
            if (_scrollViewer is not null)
                _scrollViewer.ScrollChanged -= OnScrollChanged;
            _scrollViewer = null;
            _hooked = false;
        }

        private void OnScrollChanged(object? sender, ScrollChangedEventArgs e)
        {
            if (_scrollViewer is null) return;
            if (e.ExtentHeightChange != 0)
            {
                // Items were added or removed. Follow only while the user is at the bottom.
                if (_stickToBottom) _scrollViewer.ScrollToBottom();
            }
            else
            {
                // Pure offset/viewport change (user scrolling). Recompute stickiness.
                _stickToBottom = IsAtBottom(_scrollViewer);
            }
        }

        private static bool IsAtBottom(ScrollViewer viewer) =>
            viewer.ExtentHeight - viewer.VerticalOffset - viewer.ViewportHeight < 1.5;

        private static T? FindVisualChild<T>(DependencyObject parent) where T : DependencyObject
        {
            for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is T typed) return typed;
                var descendant = FindVisualChild<T>(child);
                if (descendant is not null) return descendant;
            }
            return null;
        }
    }
}

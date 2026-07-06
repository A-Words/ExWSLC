using System.Windows;
using System.Windows.Controls;

namespace ExWSLC.Controls;

public class SelectorBarItem : ListBoxItem
{
    public static readonly DependencyProperty TextProperty =
        DependencyProperty.Register(
            nameof(Text),
            typeof(string),
            typeof(SelectorBarItem),
            new PropertyMetadata(null));

    public static readonly DependencyProperty IconProperty =
        DependencyProperty.Register(
            nameof(Icon),
            typeof(object),
            typeof(SelectorBarItem),
            new PropertyMetadata(null));

    static SelectorBarItem()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SelectorBarItem), new FrameworkPropertyMetadata(typeof(SelectorBarItem)));
    }

    public string? Text
    {
        get => (string?)GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public object? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }
}

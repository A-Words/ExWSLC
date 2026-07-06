using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;

namespace ExWSLC.Controls;

public class SelectorBar : ListBox
{
    public static readonly DependencyProperty SelectionChangedCommandProperty =
        DependencyProperty.Register(
            nameof(SelectionChangedCommand),
            typeof(ICommand),
            typeof(SelectorBar),
            new PropertyMetadata(null));

    public static readonly DependencyProperty SelectionChangedCommandParameterProperty =
        DependencyProperty.Register(
            nameof(SelectionChangedCommandParameter),
            typeof(object),
            typeof(SelectorBar),
            new PropertyMetadata(null));

    static SelectorBar()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(SelectorBar), new FrameworkPropertyMetadata(typeof(SelectorBar)));
        SelectionModeProperty.OverrideMetadata(
            typeof(SelectorBar),
            new FrameworkPropertyMetadata(SelectionMode.Single, null, CoerceSelectionMode));
    }

    public SelectorBar()
    {
        SelectionMode = SelectionMode.Single;
    }

    public ICommand? SelectionChangedCommand
    {
        get => (ICommand?)GetValue(SelectionChangedCommandProperty);
        set => SetValue(SelectionChangedCommandProperty, value);
    }

    public object? SelectionChangedCommandParameter
    {
        get => GetValue(SelectionChangedCommandParameterProperty);
        set => SetValue(SelectionChangedCommandParameterProperty, value);
    }

    protected override DependencyObject GetContainerForItemOverride() => new SelectorBarItem();

    protected override bool IsItemItsOwnContainerOverride(object item) => item is SelectorBarItem;

    protected override void OnKeyDown(KeyEventArgs eventArgs)
    {
        var handled = eventArgs.Key switch
        {
            Key.Left => MoveSelection(-1),
            Key.Right => MoveSelection(1),
            Key.Home => SelectFirstItem(),
            Key.End => SelectLastItem(),
            _ => false
        };

        if (handled)
        {
            eventArgs.Handled = true;
            return;
        }

        base.OnKeyDown(eventArgs);
    }

    protected override void OnSelectionChanged(SelectionChangedEventArgs eventArgs)
    {
        base.OnSelectionChanged(eventArgs);

        var command = SelectionChangedCommand;
        if (command is null) return;

        var parameter = SelectionChangedCommandParameter ?? SelectedItem;
        if (command.CanExecute(parameter))
        {
            command.Execute(parameter);
        }
    }

    private static object CoerceSelectionMode(DependencyObject dependencyObject, object baseValue) => SelectionMode.Single;

    private bool MoveSelection(int step)
    {
        if (Items.Count == 0) return false;

        var startIndex = SelectedIndex;
        var candidateIndex = startIndex < 0
            ? step > 0 ? 0 : Items.Count - 1
            : startIndex + step;

        while (candidateIndex >= 0 && candidateIndex < Items.Count)
        {
            if (SelectItem(candidateIndex)) return true;
            candidateIndex += step;
        }

        return false;
    }

    private bool SelectFirstItem()
    {
        for (var index = 0; index < Items.Count; index++)
        {
            if (SelectItem(index)) return true;
        }

        return false;
    }

    private bool SelectLastItem()
    {
        for (var index = Items.Count - 1; index >= 0; index--)
        {
            if (SelectItem(index)) return true;
        }

        return false;
    }

    private bool SelectItem(int index)
    {
        if (!IsSelectableIndex(index)) return false;
        if (SelectedIndex == index) return true;

        SelectedIndex = index;
        FocusContainer(index);
        return true;
    }

    private bool IsSelectableIndex(int index)
    {
        if (Items[index] is SelectorBarItem item)
        {
            return item.IsEnabled;
        }

        var container = ItemContainerGenerator.ContainerFromIndex(index) as SelectorBarItem;
        return container?.IsEnabled ?? true;
    }

    private void FocusContainer(int index)
    {
        if (ItemContainerGenerator.ContainerFromIndex(index) is SelectorBarItem container)
        {
            container.Focus();
        }
    }
}

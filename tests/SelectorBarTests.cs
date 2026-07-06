using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;
using ExWSLC.Controls;

namespace ExWSLC.Tests;

public class SelectorBarTests
{
    [Fact]
    public void SelectorBarItem_ExposesTextAndIcon()
    {
        StaTest.Run(() =>
        {
            var icon = new object();

            var item = new SelectorBarItem
            {
                Text = "Logs",
                Icon = icon
            };

            Assert.Equal("Logs", item.Text);
            Assert.Same(icon, item.Icon);
        });
    }

    [Fact]
    public void SelectorBar_CoercesSelectionModeToSingle()
    {
        StaTest.Run(() =>
        {
            var first = new SelectorBarItem { Text = "Logs" };
            var second = new SelectorBarItem { Text = "Inspect" };
            var selectorBar = new SelectorBar
            {
                SelectionMode = SelectionMode.Multiple
            };
            selectorBar.Items.Add(first);
            selectorBar.Items.Add(second);

            first.IsSelected = true;
            second.IsSelected = true;

            Assert.Equal(SelectionMode.Single, selectorBar.SelectionMode);
            Assert.False(first.IsSelected);
            Assert.True(second.IsSelected);
            Assert.Equal(1, selectorBar.SelectedIndex);
        });
    }

    [Fact]
    public void SelectorBarStyle_UsesHorizontalStackPanelItemsPanel()
    {
        StaTest.Run(() =>
        {
            var resources = LoadGenericResources();
            var style = Assert.IsType<Style>(resources[typeof(SelectorBar)]);
            var setter = style.Setters
                .OfType<Setter>()
                .Single(value => value.Property == ItemsControl.ItemsPanelProperty);
            var template = Assert.IsType<ItemsPanelTemplate>(setter.Value);

            var panel = Assert.IsType<StackPanel>(template.LoadContent());

            Assert.Equal(Orientation.Horizontal, panel.Orientation);
        });
    }

    [Fact]
    public void SelectedIndex_ExecutesSelectionChangedCommandWithSelectedItem()
    {
        StaTest.Run(() =>
        {
            var command = new RecordingCommand();
            var selectorBar = new SelectorBar
            {
                SelectionChangedCommand = command
            };
            selectorBar.Items.Add("Logs");
            selectorBar.Items.Add("Inspect");

            selectorBar.SelectedIndex = 1;

            Assert.Equal(1, command.ExecuteCount);
            Assert.Equal("Inspect", command.Parameter);
        });
    }

    [Fact]
    public void ItemsSource_CanSelectDataItem()
    {
        StaTest.Run(() =>
        {
            var selectorBar = new SelectorBar
            {
                ItemsSource = new[] { "Logs", "Inspect" }
            };

            selectorBar.SelectedIndex = 1;

            Assert.Equal("Inspect", selectorBar.SelectedItem);
        });
    }

    private sealed class RecordingCommand : ICommand
    {
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }

        public int ExecuteCount { get; private set; }

        public object? Parameter { get; private set; }

        public bool CanExecute(object? parameter) => true;

        public void Execute(object? parameter)
        {
            ExecuteCount++;
            Parameter = parameter;
        }
    }

    private static class StaTest
    {
        public static void Run(Action action)
        {
            Exception? exception = null;
            var thread = new Thread(() =>
            {
                try
                {
                    action();
                }
                catch (Exception caughtException)
                {
                    exception = caughtException;
                }
                finally
                {
                    Dispatcher.CurrentDispatcher.InvokeShutdown();
                }
            });

            thread.SetApartmentState(ApartmentState.STA);
            thread.Start();
            thread.Join();

            if (exception is not null)
            {
                ExceptionDispatchInfo.Capture(exception).Throw();
            }
        }
    }

    private static ResourceDictionary LoadGenericResources() =>
        new()
        {
            Source = new Uri("/ExWSLC;component/Themes/Generic.xaml", UriKind.Relative)
        };
}

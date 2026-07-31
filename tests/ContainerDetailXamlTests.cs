using System.IO;
using System.Runtime.ExceptionServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using ExWSLC.Models;
using ExWSLC.Views.Pages.Containers;

namespace ExWSLC.Tests;

public class ContainerDetailXamlTests
{
    [Fact]
    public void MountsDetailTemplate_LoadsMountCardWithoutMissingSymbolIconResource()
    {
        StaTest.Run(() =>
        {
            var app = new App();
            try
            {
                app.InitializeComponent();

                var view = new ContainerDetailView();
                var template = Assert.IsType<DataTemplate>(view.Resources["MountsDetailTemplate"]);
                var content = Assert.IsAssignableFrom<FrameworkElement>(template.LoadContent());
                content.DataContext = new
                {
                    IsMountDetailsLoading = false,
                    HasMountDetailsError = false,
                    MountDetailsError = string.Empty,
                    MountDetails = new ContainerMountDetails(
                    [
                        new ContainerMount("bind", @"C:\source", "/destination", true)
                    ])
                };

                content.Measure(new Size(1024, 768));
                content.Arrange(new Rect(0, 0, 1024, 768));
                content.UpdateLayout();
            }
            finally
            {
                app.Shutdown();
            }
        });
    }

    [Fact]
    public void ContainerInspectOutput_UsesOneWayBindingForReadOnlyViewModelProperty()
    {
        var xamlPath = Path.Combine(
            TestPaths.SourceDirectory,
            "Views",
            "Pages",
            "Containers",
            "ContainerDetailView.xaml");

        var xaml = File.ReadAllText(xamlPath);

        Assert.Contains("<controls:JsonTextViewer", xaml);
        Assert.Contains("JsonText=\"{Binding InspectOutput, Mode=OneWay}\"", xaml);
        Assert.Contains("AutomationProperties.Name=\"{DynamicResource Inspect}\"", xaml);
        Assert.DoesNotContain("JsonText=\"{Binding InspectOutput}\"", xaml);
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
}

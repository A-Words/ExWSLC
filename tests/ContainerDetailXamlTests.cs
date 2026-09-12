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
    public void MountAndHealthTemplates_LoadCardsWithoutMissingResources()
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

                var healthTemplate = Assert.IsType<DataTemplate>(view.Resources["HealthDetailTemplate"]);
                var healthContent = Assert.IsAssignableFrom<FrameworkElement>(healthTemplate.LoadContent());
                healthContent.DataContext = new
                {
                    IsInspectDetailsLoading = false,
                    HasInspectDetailsError = false,
                    InspectDetails = new ContainerInspectDetails("test", new ContainerInspectConfig([]), [], "{}")
                    {
                        Health = new(ContainerHealthStatus.Unhealthy, 2, [new("start", "end", 1, "not ready")])
                    }
                };
                healthContent.Measure(new Size(800, 600));
                healthContent.Arrange(new Rect(0, 0, 800, 600));
                healthContent.UpdateLayout();
                var networkTemplate = Assert.IsType<DataTemplate>(view.Resources["NetworkDetailTemplate"]);
                var networkContent = Assert.IsAssignableFrom<FrameworkElement>(networkTemplate.LoadContent());
                networkContent.Measure(new Size(800, 600));
                networkContent.Arrange(new Rect(0, 0, 800, 600));
                var createNetwork = new ExWSLC.Views.Dialogs.NetworkCreateDialogContent();
                createNetwork.Measure(new Size(540, 480));
                createNetwork.Arrange(new Rect(0, 0, 540, 480));
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

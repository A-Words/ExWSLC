using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Automation;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels.Design;
using ExWSLC.Views.Pages.Containers;

namespace ExWSLC.Tests;

public class ContainerListLayoutTests
{
    [Fact]
    public void ResizingAndScrollbarChanges_KeepHeadersAlignedAndPortDetailsAvailable()
    {
        var output = Environment.GetEnvironmentVariable("EXWSLC_UI_CONTAINER_LIST_OUTPUT");
        Assert.SkipUnless(!string.IsNullOrEmpty(output), "Run alone with EXWSLC_UI_CONTAINER_LIST_OUTPUT for WPF layout checks and offscreen images; these do not replace desktop acceptance.");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application();
            try
            {
                System.Reflection.Assembly.Load("Wpf.Ui");
                var source = XDocument.Load(Path.Combine(TestPaths.SourceDirectory, "App.xaml"));
                var resources = source.Root!.Elements().Single().Elements().Single();
                foreach (var attribute in source.Root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
                    resources.SetAttributeValue(attribute.Name, attribute.Value);
                var merged = resources.Elements().Single(element => element.Name.LocalName == "ResourceDictionary.MergedDictionaries");
                merged.Remove();
                resources.AddFirst(merged);
                app.Resources = (ResourceDictionary)XamlReader.Parse(resources.ToString()
                    .Replace("Source=\"Resources/", "Source=\"/ExWSLC;component/Resources/"));
                Directory.CreateDirectory(output!);

                var model = new DesignContainersViewModel { SearchText = string.Empty, SelectedContainer = null };
                using var workspace = model.Workspace;
                model.VisibleContainerItems.Clear();
                ContainerHealthStatus[] healthStates = [ContainerHealthStatus.NotConfigured, ContainerHealthStatus.Healthy,
                    ContainerHealthStatus.Unhealthy, ContainerHealthStatus.Starting, ContainerHealthStatus.Unknown, ContainerHealthStatus.Unhealthy];
                for (var i = 0; i < 16; i++)
                    model.VisibleContainerItems.Add(new ContainerListItem
                    {
                        Container = new ContainerSummary($"{i:000000000000}", i == 0 ? "api-gateway-production" : $"worker-{i}",
                            "localhost:5000/platform/nginx:alpine", i == 5 ? "exited" : "running", i == 5 ? "Exited" : "Up",
                            "127.0.0.1:8081->80/tcp, [::1]:8443->443/tcp", "now", healthStates[i % healthStates.Length]),
                        Stats = new ContainerStats("id", "web", "0.00%", "20.29 MiB / 8 GiB", "0 B", "0 B", "1")
                    });
                ContainerListView view = null!;
                ScrollViewer viewer = null!;
                Grid header = null!;

                foreach (var language in new[] { "en-US", "zh-CN" })
                foreach (var theme in new[] { "light", "dark" })
                {
                    var dictionaries = app.Resources.MergedDictionaries;
                    var old = dictionaries.FirstOrDefault(dictionary => dictionary.Source?.OriginalString.Contains("Strings.") == true);
                    if (old is not null) dictionaries.Remove(old);
                    dictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/ExWSLC;component/Resources/Strings.{language}.xaml") });
                    LocalizationService.ApplyTheme(theme);
                    view = new ContainerListView { DataContext = model };
                    viewer = (ScrollViewer)view.FindName("ContainerScrollViewer");
                    header = (Grid)view.FindName("ContainerHeaderGrid");
                    foreach (var width in new[] { 600d, 680d, 730d, 870d, 950d, 1250d, 1100d })
                    {
                        Arrange(view, width);
                        Render(view, Path.Combine(output!, $"{language}-{theme}-{width}.png"));
                        Assert.True(viewer.ComputedVerticalScrollBarVisibility == Visibility.Visible,
                            $"Viewport {viewer.ViewportWidth} x {viewer.ViewportHeight}, extent {viewer.ExtentHeight}, items {model.VisibleContainerItems.Count}");
                        AssertAlignment(view, header);
                        var layout = view.ListLayout;
                        Assert.Equal(viewer.ViewportWidth - 28, layout.RowWidth, 3);
                        AssertHorizontalScrolling(view, header, viewer);
                        if (layout.HasHorizontalOverflow)
                            Render(view, Path.Combine(output!, $"{language}-{theme}-{width}-scrolled.png"));
                        ((ScrollBar)view.FindName("ContainerHorizontalScrollBar")).Value = 0;
                        view.UpdateLayout();
                        AssertStatusIndicators(view);
                        var texts = Descendants(header).OfType<TextBlock>().ToArray();
                        Assert.All(texts.Where(text => Grid.GetColumn(text) is 3 or 4),
                            text => Assert.Equal(TextAlignment.Left, text.TextAlignment));
                        foreach (var row in Descendants(view).OfType<Grid>().Where(grid => grid.Name == "ContainerRowGrid"))
                        {
                            var item = Assert.IsType<ContainerListItem>(row.DataContext);
                            var cpu = row.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 3);
                            var memory = row.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 4);
                            Assert.Equal(item.Cpu, cpu.Text);
                            Assert.Equal(item.Memory, memory.Text);
                            foreach (var metric in new[] { cpu, memory })
                            {
                                Assert.Equal(Visibility.Visible, metric.Visibility);
                                Assert.Equal(TextAlignment.Left, metric.TextAlignment);
                                var metricHeader = texts.Single(text => Grid.GetColumn(text) == Grid.GetColumn(metric));
                                Assert.Equal(Visibility.Visible, metricHeader.Visibility);
                                Assert.Equal(metricHeader.TranslatePoint(new Point(), view).X, metric.TranslatePoint(new Point(), view).X, 3);
                            }
                        }
                        Assert.Equal(LocalizationService.GetString("Cpu", "CPU"), texts.Single(text => Grid.GetColumn(text) == 3).Text);
                        Assert.Equal(LocalizationService.GetString("Memory", "Memory"), texts.Single(text => Grid.GetColumn(text) == 4).Text);
                        Assert.DoesNotContain(texts, text => text.Text == LocalizationService.GetString("State", "State"));
                    }
                }

                var viewportWithScrollbar = viewer.ViewportWidth;
                while (model.VisibleContainerItems.Count > 3) model.VisibleContainerItems.RemoveAt(3);
                Arrange(view, 1100);
                Assert.Equal(Visibility.Collapsed, viewer.ComputedVerticalScrollBarVisibility);
                // WPF-UI may overlay the scrollbar rather than reserve layout space.
                Assert.True(viewer.ViewportWidth >= viewportWithScrollbar);
                AssertAlignment(view, header);
                var horizontalBar = (ScrollBar)view.FindName("ContainerHorizontalScrollBar");
                Arrange(view, 600);
                horizontalBar.Value = horizontalBar.Maximum;
                view.UpdateLayout();
                Assert.True(view.HorizontalTranslation < 0);
                Arrange(view, 1100);
                Assert.Equal(0, horizontalBar.Value);
                Assert.Equal(0, view.HorizontalTranslation);
                Assert.Equal(Visibility.Collapsed, horizontalBar.Visibility);

                // Popup requires a loaded presentation source. Keep this test-owned host offscreen.
                var host = new Window { Content = view, Width = 1100, Height = 650, Left = -10000, Top = -10000,
                    ShowInTaskbar = false, ShowActivated = false };
                Popup? popup = null;
                try
                {
                    host.Show();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    var firstRow = Descendants(view).OfType<Grid>().First(grid => grid.Name == "ContainerRowGrid");
                    var button = Descendants(firstRow).OfType<Wpf.Ui.Controls.Button>().Single(control => control.Name == "PortMappingsButton");
                    popup = Assert.IsType<Popup>(button.Tag);
                    popup.StaysOpen = true;
                    button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.True(popup.IsOpen);
                    var list = Descendants(popup.Child).OfType<ItemsControl>().Single();
                    Assert.Equal(2, list.Items.Count);
                    Assert.Null(model.SelectedContainer);
                }
                finally
                {
                    if (popup is not null) popup.IsOpen = false;
                    host.Close();
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(60)), "WPF layout verification timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Arrange(FrameworkElement view, double width)
    {
        for (var i = 0; i < 2; i++)
        {
            view.Measure(new Size(width, 650));
            view.Arrange(new Rect(0, 0, width, 650));
            view.UpdateLayout();
            Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        }
    }

    private static void AssertAlignment(FrameworkElement view, Grid header)
    {
        var rows = Descendants(view).OfType<Grid>().Where(grid => grid.Name == "ContainerRowGrid").ToArray();
        Assert.NotEmpty(rows);
        foreach (var row in rows)
        {
            Assert.Equal(header.TranslatePoint(new Point(), view).X, row.TranslatePoint(new Point(), view).X, 3);
            for (var i = 0; i < header.ColumnDefinitions.Count; i++)
                Assert.Equal(header.ColumnDefinitions[i].ActualWidth, row.ColumnDefinitions[i].ActualWidth, 3);
            var name = Descendants(row).OfType<TextBlock>().Single(text => text.Name == "ContainerNameText");
            var nameHeader = header.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == 0);
            Assert.Equal(nameHeader.TranslatePoint(new Point(), view).X, name.TranslatePoint(new Point(), view).X, 3);
        }
    }

    private static void AssertStatusIndicators(ContainerListView view)
    {
        var indicators = Descendants(view).OfType<Border>().Where(border => border.Name == "ContainerStatusIndicator").ToArray();
        Assert.Equal(16, indicators.Length);
        foreach (var indicator in indicators)
        {
            var item = Assert.IsType<ContainerListItem>(indicator.DataContext);
            var (brushKey, textKey) = item.StatusKind switch
            {
                ContainerListStatus.RunningWithoutHealthCheck => ("SystemFillColorSuccessBrush", "ContainerListRunningWithoutHealth"),
                ContainerListStatus.Healthy => ("SystemFillColorSuccessBrush", "ContainerListRunningHealthy"),
                ContainerListStatus.Unhealthy => ("SystemFillColorCautionBrush", "ContainerListRunningUnhealthy"),
                ContainerListStatus.Checking => ("ContainerCheckingIndicatorBrush", "ContainerListRunningChecking"),
                ContainerListStatus.HealthUnknown => ("ContainerCheckingIndicatorBrush", "ContainerListRunningHealthUnknown"),
                ContainerListStatus.Exited => ("TextFillColorTertiaryBrush", "ContainerListStateExited"),
                _ => throw new InvalidOperationException("Unexpected fixture state.")
            };
            var brush = Assert.IsType<SolidColorBrush>(view.FindResource(brushKey));
            Assert.Equal(brush.Color, Assert.IsType<SolidColorBrush>(indicator.Background).Color);
            Assert.Equal(LocalizationService.GetString(textKey, textKey), indicator.ToolTip);
            var name = Descendants((Grid)indicator.Parent).OfType<TextBlock>().Single(text => text.Name == "ContainerNameText");
            Assert.Equal(indicator.ToolTip, AutomationProperties.GetHelpText(name));
        }
    }

    private static void AssertHorizontalScrolling(ContainerListView view, Grid header, ScrollViewer viewer)
    {
        var scrollBar = (ScrollBar)view.FindName("ContainerHorizontalScrollBar");
        Assert.Equal(view.ListLayout.HasHorizontalOverflow ? Visibility.Visible : Visibility.Collapsed, scrollBar.Visibility);
        Assert.Equal(view.ListLayout.HorizontalScrollRange, scrollBar.Maximum, 3);
        scrollBar.Value = 0;
        view.UpdateLayout();
        var rows = Descendants(view).OfType<Grid>().Where(grid => grid.Name == "ContainerRowGrid").ToArray();
        var actions = Descendants(view).OfType<StackPanel>().Where(panel => panel.Name == "ContainerRowActions").ToArray();
        Assert.Equal(rows.Length, actions.Length);
        var origin = rows[0].TranslatePoint(new Point(), view).X;
        var actionOrigins = actions.Select(panel => panel.TranslatePoint(new Point(), view).X).ToArray();
        var actionHeader = (TextBlock)view.FindName("ContainerActionsHeader");
        var headerRight = actionHeader.TranslatePoint(new Point(actionHeader.ActualWidth, 0), view).X;
        var viewportRight = viewer.TranslatePoint(new Point(viewer.ViewportWidth - 14, 0), view).X;
        Assert.Equal(viewportRight, headerRight, 3);
        if (scrollBar.Visibility == Visibility.Visible)
        {
            Assert.Equal(origin, scrollBar.TranslatePoint(new Point(), view).X, 3);
            Assert.Equal(viewportRight, scrollBar.TranslatePoint(new Point(scrollBar.ActualWidth, 0), view).X, 3);
            Assert.Equal(view.ListLayout.DataViewport, scrollBar.ViewportSize, 3);
        }

        scrollBar.Value = scrollBar.Maximum;
        view.UpdateLayout();
        Assert.Equal(-scrollBar.Maximum, view.HorizontalTranslation, 3);
        Assert.Equal(origin - scrollBar.Maximum, rows[0].TranslatePoint(new Point(), view).X, 3);
        AssertAlignment(view, header);
        // Visibility alone does not detect WPF layout clipping after translating a wide child.
        foreach (var column in new[] { 3, 4 })
        {
            var metric = rows[0].Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == column);
            var metricHeader = header.Children.OfType<TextBlock>().Single(text => Grid.GetColumn(text) == column);
            foreach (var text in new[] { metric, metricHeader })
                Assert.Same(text, VisualTreeHelper.HitTest(view, text.TranslatePoint(new Point(2, text.ActualHeight / 2), view))?.VisualHit);
        }
        for (var i = 0; i < actions.Length; i++)
        {
            var action = actions[i];
            Assert.Equal(actionOrigins[i], action.TranslatePoint(new Point(), view).X, 3);
            Assert.Equal(viewportRight, action.TranslatePoint(new Point(action.ActualWidth, 0), view).X, 3);
            var buttons = action.Children.OfType<Wpf.Ui.Controls.Button>().Where(button => button.Visibility == Visibility.Visible).ToArray();
            Assert.Equal(2, buttons.Length);
            foreach (var button in buttons)
            {
                Assert.Equal(34, button.ActualWidth, 3);
                var bounds = button.TransformToAncestor(view).TransformBounds(new Rect(button.RenderSize));
                Assert.True(bounds.Left >= actionOrigins[i]);
                Assert.True(bounds.Right <= viewportRight + .01);
            }
            var clip = (Border)((Canvas)rows[i].Parent).Parent;
            Assert.True(clip.ClipToBounds);
            Assert.True(clip.TranslatePoint(new Point(clip.ActualWidth, 0), view).X <= actionOrigins[i]);
        }
        Assert.Equal(headerRight, actionHeader.TranslatePoint(new Point(actionHeader.ActualWidth, 0), view).X, 3);
    }

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = VisualTreeHelper.GetChild(parent, i);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Render(FrameworkElement view, string path)
    {
        var bitmap = new RenderTargetBitmap((int)view.ActualWidth, (int)view.ActualHeight, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle((Brush)Application.Current.FindResource("ApplicationBackgroundBrush"), null,
                new Rect(0, 0, view.ActualWidth, view.ActualHeight));
        }
        bitmap.Render(drawing);
        bitmap.Render(view);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}

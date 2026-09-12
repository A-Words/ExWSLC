using System.IO;
using System.Runtime.ExceptionServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Windows.Markup;
using System.Xml.Linq;
using ExWSLC.Services;
using ExWSLC.ViewModels.Design;
using ExWSLC.Views.Pages;

namespace ExWSLC.Tests;

public class ImageBuildUiTests
{
    [Fact]
    public void BuildForm_RendersLanguagesAndThemesAndUpdatesOutputControls()
    {
        var output = Environment.GetEnvironmentVariable("EXWSLC_UI_06_OUTPUT");
        Assert.SkipUnless(!string.IsNullOrEmpty(output), "Run this class alone with EXWSLC_UI_06_OUTPUT set for offscreen WPF rendering (not desktop acceptance).");
        Exception? failure = null;
        var thread = new Thread(() =>
        {
            var app = new Application();
            try
            {
                // Load the real resource dictionary without starting the application's services.
                System.Reflection.Assembly.Load("Wpf.Ui");
                var source = XDocument.Load(Path.Combine(TestPaths.SourceDirectory, "App.xaml"));
                var resources = source.Root!.Elements().Single().Elements().Single();
                foreach (var attribute in source.Root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration))
                    resources.SetAttributeValue(attribute.Name, attribute.Value);
                var merged = resources.Elements().Single(element => element.Name.LocalName == "ResourceDictionary.MergedDictionaries");
                merged.Remove();
                resources.AddFirst(merged);
                var resourceXaml = resources.ToString().Replace("Source=\"Resources/", "Source=\"/ExWSLC;component/Resources/");
                app.Resources = (ResourceDictionary)XamlReader.Parse(resourceXaml);
                Directory.CreateDirectory(output!);
                foreach (var language in new[] { "en-US", "zh-CN" })
                foreach (var theme in new[] { "system", "light", "dark" })
                {
                    var dictionaries = app.Resources.MergedDictionaries;
                    var current = dictionaries.FirstOrDefault(dictionary => dictionary.Source?.OriginalString.Contains("Strings.") == true);
                    if (current is not null) dictionaries.Remove(current);
                    dictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/ExWSLC;component/Resources/Strings.{language}.xaml") });
                    LocalizationService.ApplyTheme(theme);
                    var model = new DesignImagesViewModel { BuildContextPath = @"C:\项目 context", BuildImageTag = "example:test" };
                    using var workspace = model.Workspace;
                    if (Environment.GetEnvironmentVariable("EXWSLC_UI_06_BASELINE") is { Length: > 0 } baselinePath)
                    {
                        var baseline = XDocument.Load(baselinePath);
                        baseline.Root!.Attribute(XName.Get("Class", "http://schemas.microsoft.com/winfx/2006/xaml"))!.Remove();
                        foreach (var handler in baseline.Descendants().Attributes().Where(attribute => attribute.Name.LocalName == "Click").ToArray()) handler.Remove();
                        var before = (Page)XamlReader.Parse(baseline.ToString());
                        before.DataContext = model;
                        before.SetResourceReference(Page.BackgroundProperty, "ApplicationBackgroundBrush");
                        ((Expander)before.FindName("ImageOperationsExpander")).IsExpanded = true;
                        Render(before, Path.Combine(output!, $"{language}-{theme}-before.png"));
                    }
                    var page = new ImagesPage(model);
                    page.SetResourceReference(Page.BackgroundProperty, "ApplicationBackgroundBrush");
                    ((Expander)page.FindName("ImageOperationsExpander")).IsExpanded = true;
                    var advanced = (Expander)page.FindName("AdvancedBuildExpander");
                    Assert.False(advanced.IsExpanded);
                    Render(page, Path.Combine(output!, $"{language}-{theme}-simple.png"));
                    advanced.IsExpanded = true;
                    model.BuildOutputMode = 1;
                    model.BuildOutputPath = @"C:\产物\rootfs.tar";
                    Render(page, Path.Combine(output!, $"{language}-{theme}-advanced.png"));
                    Render(page, Path.Combine(output!, $"{language}-{theme}-output.png"), 420);
                    Render(page, Path.Combine(output!, $"{language}-{theme}-secrets.png"), 820);
                    Assert.False(((FrameworkElement)page.FindName("BuildImageTagLabel")).IsEnabled);
                    model.BuildOutputMode = 0;
                    page.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.True(((FrameworkElement)page.FindName("BuildImageTagLabel")).IsEnabled);
                }
            }
            catch (Exception exception) { failure = exception; }
            finally { app.Shutdown(); Dispatcher.CurrentDispatcher.InvokeShutdown(); }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        Assert.True(thread.Join(TimeSpan.FromSeconds(45)), "WPF rendering timed out.");
        if (failure is not null) ExceptionDispatchInfo.Capture(failure).Throw();
    }

    private static void Render(FrameworkElement page, string path, double offset = 0)
    {
        // Approximate content area inside the default 1100 x 800 shell.
        page.Measure(new Size(1000, 700));
        page.Arrange(new Rect(0, 0, 1000, 700));
        page.UpdateLayout();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
        var frame = new DispatcherFrame();
        var timer = new DispatcherTimer(DispatcherPriority.Background) { Interval = TimeSpan.FromMilliseconds(350) };
        timer.Tick += (_, _) => { timer.Stop(); frame.Continue = false; };
        timer.Start();
        Dispatcher.PushFrame(frame);
        if (page.FindName("ImageOperationsScrollViewer") is ScrollViewer scroll) scroll.ScrollToVerticalOffset(offset);
        page.UpdateLayout();
        var bitmap = new RenderTargetBitmap(1000, 700, 96, 96, PixelFormats.Pbgra32);
        // The loose baseline Page has no shell template to paint its background.
        var background = new DrawingVisual();
        using (var drawing = background.RenderOpen())
            drawing.DrawRectangle((Brush)Application.Current.FindResource("ApplicationBackgroundBrush"), null, new Rect(0, 0, 1000, 700));
        bitmap.Render(background);
        bitmap.Render(page);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}

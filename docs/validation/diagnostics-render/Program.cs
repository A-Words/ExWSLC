using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using System.Xml.Linq;
using System.Text.Json;
using ExWSLC.Models;
using ExWSLC.Services;
using ExWSLC.ViewModels.Design;
using ExWSLC.Views.Pages;

// Run from the repository root. Renders the real SettingsPage without starting
// App.OnStartup, inventory polling, opening a window, or changing preferences.
internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        if (Environment.GetEnvironmentVariable("EXWSLC_LIVE_DIAGNOSTICS") != "1")
            throw new InvalidOperationException("Set EXWSLC_LIVE_DIAGNOSTICS=1 to allow read-only WSLC queries.");
        var output = args.Length > 0 ? args[0] : "artifacts/diagnostics-render";
        Directory.CreateDirectory(output);
        var app = new RenderApp();
        Assembly.Load("Wpf.Ui");
        var document = XDocument.Load("src/App.xaml");
        var dictionary = document.Descendants().First(element => element.Name.LocalName == "ResourceDictionary");
        dictionary.SetAttributeValue(XNamespace.Xmlns + "x", "http://schemas.microsoft.com/winfx/2006/xaml");
        dictionary.SetAttributeValue(XNamespace.Xmlns + "ui", "http://schemas.lepo.co/wpfui/2022/xaml");
        var merged = dictionary.Elements().First(element => element.Name.LocalName == "ResourceDictionary.MergedDictionaries");
        merged.Remove();
        dictionary.AddFirst(merged);
        app.Resources = (ResourceDictionary)XamlReader.Parse(dictionary.ToString()
            .Replace("Source=\"Resources/", "Source=\"/ExWSLC;component/Resources/"));

        var runner = new WslcProcessRunner();
        var capabilities = Task.Run(() => new RuntimeCapabilityService(runner, new WslcSdkService()).DetectAsync()).GetAwaiter().GetResult();
        var snapshot = Task.Run(() => new WslcContainerRuntime(runner).GetSystemInfoAsync(capabilities)).GetAwaiter().GetResult();
        if (snapshot.StatusKey != "DiagnosticsCollected") throw new InvalidOperationException(snapshot.StatusKey);
        // An optional task-09 result file is produced by the opt-in live test after
        // a connection to its own loopback listener. Rendering never starts a probe.
        var hostProbe = args.Length > 1 ? JsonSerializer.Deserialize<HostLoopbackProbeResult>(File.ReadAllText(args[1])) : null;
        var hostConfiguration = hostProbe is null ? null : Task.Run(() => new WslcContainerRuntime(runner).GetHostLoopbackConfigurationAsync()).GetAwaiter().GetResult();

        foreach (var language in new[] { "en-US", "zh-CN" })
        foreach (var theme in new[] { "Light", "Dark", "System" })
        {
            var dictionaries = app.Resources.MergedDictionaries;
            var old = dictionaries.FirstOrDefault(value => value.Source?.OriginalString.Contains("Strings.") == true);
            if (old is not null) dictionaries.Remove(old);
            dictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/ExWSLC;component/Resources/Strings.{language}.xaml") });
            LocalizationService.ApplyTheme(theme);
            Capture(snapshot, capabilities, 850, $"after-{language}-{theme}");
            if (theme == "Light") Capture(snapshot, capabilities, 550, $"narrow-{language}-{theme}");
        }
        Console.WriteLine($"Rendered CLI {snapshot.SystemInfo?.ClientVersion}, service {snapshot.SystemInfo?.ServiceVersion}, SDK {snapshot.SdkPackageVersion}.");

        void Capture(RuntimeDiagnostics data, RuntimeCapabilities baseline, int width, string name)
        {
            var viewModel = new DesignSettingsViewModel { Diagnostics = data };
            viewModel.Workspace.Capabilities = baseline;
            if (hostProbe is not null)
            {
                var container = new ContainerSummary(hostProbe.Target.ContainerId, "exwslc-09-test-snapshot", "test snapshot", "running", "", "", "");
                viewModel.Workspace.ActiveContainers.Clear();
                viewModel.Workspace.ActiveContainers.Add(container);
                viewModel.HostLoopbackConfiguration = hostConfiguration;
                viewModel.HostProbeContainer = container;
                viewModel.HostProbePort = hostProbe.Target.Port.ToString(System.Globalization.CultureInfo.InvariantCulture);
                viewModel.HostProbeResult = hostProbe;
            }
            var page = new SettingsPage(viewModel)
            {
                Background = (Brush)app.FindResource("ApplicationBackgroundBrush"), Width = width, Height = 700
            };
            page.Measure(new Size(width, 700));
            page.Arrange(new Rect(0, 0, width, 700));
            page.UpdateLayout();
            app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
            var scroll = (ScrollViewer)page.FindName("SettingsScrollViewer");
            var card = (FrameworkElement)page.FindName(hostProbe is null ? "DiagnosticsCard" : "HostLoopbackCard");
            var offset = card.TranslatePoint(new Point(), (UIElement)scroll.Content).Y;
            Save(offset, name);
            if (width < 850) Save(offset + 400, name + "-bottom");
            viewModel.Workspace.Dispose();

            void Save(double verticalOffset, string fileName)
            {
                scroll.ScrollToVerticalOffset(verticalOffset);
                page.UpdateLayout();
                app.Dispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                var bitmap = new RenderTargetBitmap(width, 700, 96, 96, PixelFormats.Pbgra32);
                bitmap.Render(page);
                using var file = File.Create(Path.Combine(output, fileName + ".png"));
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(bitmap));
                encoder.Save(file);
            }
        }
    }
}

internal sealed class RenderApp : ExWSLC.App
{
    protected override void OnStartup(StartupEventArgs e) { }
}

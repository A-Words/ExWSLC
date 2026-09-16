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

public class NativeSettingsUiTests
{
    [Fact]
    public void SettingsForm_RendersTypedControlsAndUpdatesBindings()
    {
        var output = Environment.GetEnvironmentVariable("EXWSLC_UI_SETTINGS_OUTPUT");
        Assert.SkipUnless(!string.IsNullOrEmpty(output), "Run this class alone with EXWSLC_UI_SETTINGS_OUTPUT set for offscreen WPF rendering (not desktop acceptance).");
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
                foreach (var theme in new[] { "light", "dark" })
                {
                    var dictionaries = app.Resources.MergedDictionaries;
                    var current = dictionaries.FirstOrDefault(dictionary => dictionary.Source?.OriginalString.Contains("Strings.") == true);
                    if (current is not null) dictionaries.Remove(current);
                    dictionaries.Add(new ResourceDictionary { Source = new Uri($"pack://application:,,,/ExWSLC;component/Resources/Strings.{language}.xaml") });
                    LocalizationService.ApplyTheme(theme);
                    var model = new DesignSettingsViewModel();
                    using var workspace = model.Workspace;
                    model.LoadNativeSettingsCommand.ExecuteAsync(null).GetAwaiter().GetResult();
                    var page = new SettingsPage { DataContext = model };
                    page.Measure(new Size(1000, 700));
                    page.Arrange(new Rect(0, 0, 1000, 700));
                    page.UpdateLayout();
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.Equal(12, model.NativeSettingsFields.Count);
                    Assert.False(model.CanSaveNativeSettings);
                    var cpu = model.NativeSettingsFields.Single(setting => setting.Definition.Key == "Cpu");
                    cpu.UseDefault = false;
                    cpu.NumericValue = 4;
                    page.UpdateLayout();
                    Assert.True(model.CanSaveNativeSettings);
                    var card = (FrameworkElement)page.FindName("NativeConfigurationCard");
                    var controls = Descendants(card).ToArray();
                    Assert.Equal(2, controls.OfType<Wpf.Ui.Controls.NumberBox>().Count());
                    Assert.Equal(4, controls.OfType<ComboBox>().Count());
                    var dnsToggle = Assert.Single(controls.OfType<Wpf.Ui.Controls.ToggleSwitch>());
                    var dns = model.NativeSettingsFields.Single(setting => setting.Definition.Key == "Dns");
                    dns.UseDefault = false;
                    dns.ToggleValue = true;
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.True(dnsToggle.IsChecked);
                    dnsToggle.SetCurrentValue(System.Windows.Controls.Primitives.ToggleButton.IsCheckedProperty, (bool?)false);
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.False(dns.ToggleValue);
                    var cpuInput = controls.OfType<Wpf.Ui.Controls.NumberBox>().Single(box => ReferenceEquals(box.DataContext, cpu));
                    cpuInput.SetCurrentValue(Wpf.Ui.Controls.NumberBox.ValueProperty, 6d);
                    Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                    Assert.Equal(6, cpu.NumericValue);
                    foreach (var numberInput in controls.OfType<Wpf.Ui.Controls.NumberBox>())
                    {
                        var setting = (ExWSLC.ViewModels.NativeSettingViewModel)numberInput.DataContext;
                        setting.UseDefault = false;
                        numberInput.SetCurrentValue(Wpf.Ui.Controls.NumberBox.ValueProperty, 6d);
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        // Exercise NumberBox's actual empty-text commit, not just the model setter.
                        numberInput.SetCurrentValue(TextBox.TextProperty, string.Empty);
                        typeof(Wpf.Ui.Controls.NumberBox).GetMethod("ValidateInput",
                            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(numberInput, null);
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        Assert.Null(numberInput.Value);
                        Assert.Null(setting.NumericValue);
                        Assert.Equal(string.Empty, setting.Value);
                        Assert.False(Validation.GetHasError(numberInput));
                        Assert.False(model.SaveNativeSettingsCommand.CanExecute(null));
                        numberInput.SetCurrentValue(Wpf.Ui.Controls.NumberBox.ValueProperty, 6d);
                        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.ApplicationIdle);
                        Assert.True(model.SaveNativeSettingsCommand.CanExecute(null));
                    }
                    var memory = model.NativeSettingsFields.Single(setting => setting.Definition.Key == "Memory");
                    memory.UseDefault = false;
                    memory.Value = "invalid";
                    Assert.False(model.CanSaveNativeSettings);
                    memory.Value = "4GB";
                    Assert.True(model.CanSaveNativeSettings);
                    Render(card, Path.Combine(output!, $"{language}-{theme}-form.png"));
                    CommunityToolkit.Mvvm.Messaging.WeakReferenceMessenger.Default.UnregisterAll(model);
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

    private static IEnumerable<DependencyObject> Descendants(DependencyObject parent)
    {
        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(parent); index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Render(FrameworkElement card, string path)
    {
        card.UpdateLayout();
        var width = (int)Math.Ceiling(card.ActualWidth);
        var height = (int)Math.Ceiling(card.ActualHeight);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var drawing = new DrawingVisual();
        using (var context = drawing.RenderOpen())
        {
            context.DrawRectangle((Brush)Application.Current.FindResource("ApplicationBackgroundBrush"), null, new Rect(0, 0, width, height));
            context.DrawRectangle(new VisualBrush(card), null, new Rect(0, 0, width, height));
        }
        bitmap.Render(drawing);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var file = File.Create(path);
        encoder.Save(file);
    }
}

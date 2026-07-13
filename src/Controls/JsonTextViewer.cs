using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using ExWSLC.Helpers;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Folding;

namespace ExWSLC.Controls;

public sealed class JsonTextViewer : TextEditor
{
    public static readonly DependencyProperty JsonTextProperty =
        DependencyProperty.Register(
            nameof(JsonText),
            typeof(string),
            typeof(JsonTextViewer),
            new FrameworkPropertyMetadata(string.Empty, OnJsonTextChanged));

    private readonly FoldingManager _foldingManager;

    public JsonTextViewer()
    {
        IsReadOnly = true;
        ShowLineNumbers = true;
        WordWrap = false;
        FontFamily = new FontFamily("Consolas");
        FontSize = 13;
        HorizontalScrollBarVisibility = ScrollBarVisibility.Auto;
        VerticalScrollBarVisibility = ScrollBarVisibility.Auto;
        _foldingManager = FoldingManager.Install(TextArea);
        ReplaceDefaultFoldingMargin();
    }

    public string JsonText
    {
        get => (string)GetValue(JsonTextProperty);
        set => SetValue(JsonTextProperty, value);
    }

    internal IEnumerable<FoldingSection> Foldings => _foldingManager.AllFoldings;

    private static void OnJsonTextChanged(DependencyObject dependencyObject, DependencyPropertyChangedEventArgs eventArgs)
    {
        var viewer = (JsonTextViewer)dependencyObject;
        viewer.UpdateText(eventArgs.NewValue as string);
    }

    private void UpdateText(string? jsonText)
    {
        Text = jsonText ?? string.Empty;
        _foldingManager.UpdateFoldings(JsonFoldingStrategy.CreateFoldings(Text), -1);

        foreach (var folding in _foldingManager.AllFoldings)
        {
            folding.IsFolded = false;
        }

        CaretOffset = 0;
        ScrollToHome();
    }

    private void ReplaceDefaultFoldingMargin()
    {
        var defaultMargin = TextArea.LeftMargins.OfType<FoldingMargin>().Single();
        TextArea.LeftMargins.Remove(defaultMargin);

        var fluentMargin = new FluentFoldingMargin(_foldingManager);
        fluentMargin.SetResourceReference(FluentFoldingMargin.MarkerBrushProperty, "TextFillColorSecondaryBrush");
        fluentMargin.SetResourceReference(FluentFoldingMargin.HoverMarkerBrushProperty, "TextFillColorPrimaryBrush");
        fluentMargin.SetResourceReference(FluentFoldingMargin.HoverBackgroundBrushProperty, "SubtleFillColorSecondaryBrush");
        TextArea.LeftMargins.Add(fluentMargin);
    }
}

using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using ICSharpCode.AvalonEdit.Editing;
using ICSharpCode.AvalonEdit.Folding;
using ICSharpCode.AvalonEdit.Rendering;

namespace ExWSLC.Controls;

internal sealed class FluentFoldingMargin : AbstractMargin
{
    public static readonly DependencyProperty MarkerBrushProperty =
        DependencyProperty.Register(
            nameof(MarkerBrush),
            typeof(Brush),
            typeof(FluentFoldingMargin),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoverMarkerBrushProperty =
        DependencyProperty.Register(
            nameof(HoverMarkerBrush),
            typeof(Brush),
            typeof(FluentFoldingMargin),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty HoverBackgroundBrushProperty =
        DependencyProperty.Register(
            nameof(HoverBackgroundBrush),
            typeof(Brush),
            typeof(FluentFoldingMargin),
            new FrameworkPropertyMetadata(Brushes.Transparent, FrameworkPropertyMetadataOptions.AffectsRender));

    private const double MarginWidth = 18;
    private const double MarkerSize = 14;
    private const double ChevronRadius = 3;
    private readonly FoldingManager _foldingManager;
    private readonly List<FoldingMarker> _markers = [];
    private FoldingSection? _hoveredSection;

    public FluentFoldingMargin(FoldingManager foldingManager)
    {
        _foldingManager = foldingManager;
        Cursor = Cursors.Arrow;
        SnapsToDevicePixels = true;
    }

    public Brush MarkerBrush
    {
        get => (Brush)GetValue(MarkerBrushProperty);
        set => SetValue(MarkerBrushProperty, value);
    }

    public Brush HoverMarkerBrush
    {
        get => (Brush)GetValue(HoverMarkerBrushProperty);
        set => SetValue(HoverMarkerBrushProperty, value);
    }

    public Brush HoverBackgroundBrush
    {
        get => (Brush)GetValue(HoverBackgroundBrushProperty);
        set => SetValue(HoverBackgroundBrushProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize) => new(MarginWidth, 0);

    protected override void OnTextViewChanged(TextView? oldTextView, TextView? newTextView)
    {
        if (oldTextView is not null)
        {
            oldTextView.VisualLinesChanged -= OnVisualLinesChanged;
            oldTextView.ScrollOffsetChanged -= OnScrollOffsetChanged;
        }

        base.OnTextViewChanged(oldTextView, newTextView);

        if (newTextView is not null)
        {
            newTextView.VisualLinesChanged += OnVisualLinesChanged;
            newTextView.ScrollOffsetChanged += OnScrollOffsetChanged;
        }

        InvalidateVisual();
    }

    protected override void OnRender(DrawingContext drawingContext)
    {
        base.OnRender(drawingContext);
        drawingContext.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));
        _markers.Clear();

        if (TextView is null || !TextView.VisualLinesValid) return;

        foreach (var visualLine in TextView.VisualLines)
        {
            var folding = _foldingManager.GetNextFolding(visualLine.FirstDocumentLine.Offset);
            if (folding is null || folding.StartOffset > visualLine.LastDocumentLine.EndOffset) continue;

            var relativeOffset = folding.StartOffset - visualLine.FirstDocumentLine.Offset;
            var visualColumn = visualLine.GetVisualColumn(relativeOffset);
            var markerCenterY = visualLine.GetVisualPosition(visualColumn, VisualYPosition.TextMiddle).Y -
                                TextView.VerticalOffset;
            var markerBounds = new Rect(
                (RenderSize.Width - MarkerSize) / 2,
                markerCenterY - MarkerSize / 2,
                MarkerSize,
                MarkerSize);

            _markers.Add(new FoldingMarker(markerBounds, folding));
            DrawMarker(drawingContext, markerBounds, folding);
        }
    }

    protected override void OnMouseMove(MouseEventArgs eventArgs)
    {
        base.OnMouseMove(eventArgs);
        var hoveredSection = FindFolding(eventArgs.GetPosition(this));
        if (ReferenceEquals(hoveredSection, _hoveredSection)) return;

        _hoveredSection = hoveredSection;
        Cursor = hoveredSection is null ? Cursors.Arrow : Cursors.Hand;
        InvalidateVisual();
    }

    protected override void OnMouseLeave(MouseEventArgs eventArgs)
    {
        base.OnMouseLeave(eventArgs);
        if (_hoveredSection is null) return;

        _hoveredSection = null;
        Cursor = Cursors.Arrow;
        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs eventArgs)
    {
        var folding = FindFolding(eventArgs.GetPosition(this));
        if (folding is null)
        {
            base.OnMouseLeftButtonDown(eventArgs);
            return;
        }

        folding.IsFolded = !folding.IsFolded;
        eventArgs.Handled = true;
    }

    private void DrawMarker(DrawingContext drawingContext, Rect markerBounds, FoldingSection folding)
    {
        var isHovered = ReferenceEquals(folding, _hoveredSection);
        if (isHovered)
        {
            drawingContext.DrawRoundedRectangle(HoverBackgroundBrush, null, markerBounds, 4, 4);
        }

        var pen = new Pen(isHovered ? HoverMarkerBrush : MarkerBrush, 1.5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round,
            LineJoin = PenLineJoin.Round
        };
        var centerX = markerBounds.Left + markerBounds.Width / 2;
        var centerY = markerBounds.Top + markerBounds.Height / 2;

        if (folding.IsFolded)
        {
            drawingContext.DrawLine(
                pen,
                new Point(centerX - ChevronRadius / 2, centerY - ChevronRadius),
                new Point(centerX + ChevronRadius / 2, centerY));
            drawingContext.DrawLine(
                pen,
                new Point(centerX + ChevronRadius / 2, centerY),
                new Point(centerX - ChevronRadius / 2, centerY + ChevronRadius));
            return;
        }

        drawingContext.DrawLine(
            pen,
            new Point(centerX - ChevronRadius, centerY - ChevronRadius / 2),
            new Point(centerX, centerY + ChevronRadius / 2));
        drawingContext.DrawLine(
            pen,
            new Point(centerX, centerY + ChevronRadius / 2),
            new Point(centerX + ChevronRadius, centerY - ChevronRadius / 2));
    }

    private FoldingSection? FindFolding(Point position)
    {
        foreach (var marker in _markers)
        {
            if (marker.Bounds.Contains(position)) return marker.Section;
        }

        return null;
    }

    private void OnVisualLinesChanged(object? sender, EventArgs eventArgs) => InvalidateVisual();

    private void OnScrollOffsetChanged(object? sender, EventArgs eventArgs) => InvalidateVisual();

    private readonly record struct FoldingMarker(Rect Bounds, FoldingSection Section);
}

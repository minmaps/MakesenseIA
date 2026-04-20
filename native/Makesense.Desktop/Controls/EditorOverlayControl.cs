using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.Controls;

public sealed class AnnotationRecordEventArgs : EventArgs
{
    public AnnotationRecordEventArgs(AnnotationRecord annotation)
    {
        Annotation = annotation;
    }

    public AnnotationRecord Annotation { get; }
}

public sealed class AnnotationSelectionEventArgs : EventArgs
{
    public AnnotationSelectionEventArgs(string? annotationId)
    {
        AnnotationId = annotationId;
    }

    public string? AnnotationId { get; }
}

public sealed class ViewportPointerEventArgs : EventArgs
{
    public ViewportPointerEventArgs(Point viewportPoint)
    {
        ViewportPoint = viewportPoint;
    }

    public Point ViewportPoint { get; }
}

public sealed class ViewportTransformChangedEventArgs : EventArgs
{
    public ViewportTransformChangedEventArgs(double zoom, double panOffsetX, double panOffsetY)
    {
        Zoom = zoom;
        PanOffsetX = panOffsetX;
        PanOffsetY = panOffsetY;
    }

    public double Zoom { get; }

    public double PanOffsetX { get; }

    public double PanOffsetY { get; }
}

internal enum OverlayInteractionMode
{
    None,
    Creating,
    Moving,
    ResizingRectTopLeft,
    ResizingRectTopRight,
    ResizingRectBottomLeft,
    ResizingRectBottomRight,
    MovingPoint,
    MovingLineStart,
    MovingLineEnd,
    MovingPolygonVertex,
    Panning
}

public sealed class EditorOverlayControl : Canvas
{
    public static readonly DependencyProperty ImageProperty =
        DependencyProperty.Register(nameof(Image), typeof(ImageRecord), typeof(EditorOverlayControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty AnnotationsProperty =
        DependencyProperty.Register(nameof(Annotations), typeof(IEnumerable<AnnotationRecord>), typeof(EditorOverlayControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty SelectedAnnotationIdProperty =
        DependencyProperty.Register(nameof(SelectedAnnotationId), typeof(string), typeof(EditorOverlayControl),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ToolProperty =
        DependencyProperty.Register(nameof(Tool), typeof(AnnotationTool), typeof(EditorOverlayControl),
            new FrameworkPropertyMetadata(AnnotationTool.Rect, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ZoomProperty =
        DependencyProperty.Register(nameof(Zoom), typeof(double), typeof(EditorOverlayControl),
            new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PanOffsetXProperty =
        DependencyProperty.Register(nameof(PanOffsetX), typeof(double), typeof(EditorOverlayControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty PanOffsetYProperty =
        DependencyProperty.Register(nameof(PanOffsetY), typeof(double), typeof(EditorOverlayControl),
            new FrameworkPropertyMetadata(0.0, FrameworkPropertyMetadataOptions.AffectsRender));

    private readonly Brush _fillBrush = new SolidColorBrush(Color.FromArgb(56, 79, 209, 197));
    private readonly Pen _shapePen = new(new SolidColorBrush(Color.FromRgb(79, 209, 197)), 2.0);
    private readonly Pen _selectedPen = new(new SolidColorBrush(Color.FromRgb(255, 196, 0)), 3.0);
    private readonly Brush _suggestionFillBrush = new SolidColorBrush(Color.FromArgb(40, 255, 112, 67));
    private readonly Pen _suggestionPen = new(new SolidColorBrush(Color.FromRgb(255, 112, 67)), 2.0) { DashStyle = DashStyles.Dash };
    private readonly Brush _pointBrush = new SolidColorBrush(Color.FromRgb(255, 255, 255));
    private readonly Brush _handleBrush = new SolidColorBrush(Color.FromRgb(255, 196, 0));
    private readonly List<Point2D> _pendingPolygon = [];

    private OverlayInteractionMode _interactionMode = OverlayInteractionMode.None;
    private Point? _dragStartViewport;
    private Point2D? _dragStartImage;
    private AnnotationRecord? _dragSeedAnnotation;
    private AnnotationRecord? _previewAnnotation;
    private Point? _panOriginViewport;
    private double _panOriginX;
    private double _panOriginY;
    private int _activeVertexIndex = -1;

    public EditorOverlayControl()
    {
        Background = Brushes.Transparent;
        ClipToBounds = true;
        Focusable = true;
    }

    public event EventHandler<AnnotationRecordEventArgs>? AnnotationCreated;

    public event EventHandler<AnnotationRecordEventArgs>? AnnotationUpdated;

    public event EventHandler<AnnotationSelectionEventArgs>? AnnotationSelectionChanged;

    public event EventHandler<ViewportPointerEventArgs>? ViewportPointerMoved;

    public event EventHandler<ViewportTransformChangedEventArgs>? ViewportTransformChanged;

    public ImageRecord? Image
    {
        get => (ImageRecord?)GetValue(ImageProperty);
        set => SetValue(ImageProperty, value);
    }

    public IEnumerable<AnnotationRecord> Annotations
    {
        get => (IEnumerable<AnnotationRecord>?)GetValue(AnnotationsProperty) ?? Array.Empty<AnnotationRecord>();
        set => SetValue(AnnotationsProperty, value);
    }

    public string? SelectedAnnotationId
    {
        get => (string?)GetValue(SelectedAnnotationIdProperty);
        set => SetValue(SelectedAnnotationIdProperty, value);
    }

    public AnnotationTool Tool
    {
        get => (AnnotationTool)GetValue(ToolProperty);
        set => SetValue(ToolProperty, value);
    }

    public double Zoom
    {
        get => (double)GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    public double PanOffsetX
    {
        get => (double)GetValue(PanOffsetXProperty);
        set => SetValue(PanOffsetXProperty, value);
    }

    public double PanOffsetY
    {
        get => (double)GetValue(PanOffsetYProperty);
        set => SetValue(PanOffsetYProperty, value);
    }

    public void CancelPendingGeometry()
    {
        _pendingPolygon.Clear();
        _previewAnnotation = null;
        _dragStartViewport = null;
        _dragStartImage = null;
        _dragSeedAnnotation = null;
        _interactionMode = OverlayInteractionMode.None;
        _activeVertexIndex = -1;
        ReleaseMouseCapture();
        InvalidateVisual();
    }

    public void FinalizePendingPolygon()
    {
        if (_pendingPolygon.Count < 3)
        {
            return;
        }

        AnnotationCreated?.Invoke(this, new AnnotationRecordEventArgs(new AnnotationRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = AnnotationKind.Polygon,
            Polygon = _pendingPolygon.ToArray()
        }));

        _pendingPolygon.Clear();
        InvalidateVisual();
    }

    public void ZoomAtCenter(double factor)
    {
        var center = new Point(ActualWidth / 2.0, ActualHeight / 2.0);
        ApplyZoom(factor, center);
    }

    protected override void OnRender(DrawingContext dc)
    {
        base.OnRender(dc);

        var imageRect = GetImageViewportRect();
        if (imageRect is null)
        {
            return;
        }

        dc.DrawRectangle(null, new Pen(new SolidColorBrush(Color.FromArgb(80, 255, 255, 255)), 1), imageRect.Value);

        foreach (var annotation in Annotations)
        {
            if (!annotation.IsVisible)
            {
                continue;
            }

            var isSelected = annotation.Id == SelectedAnnotationId;
            DrawAnnotation(dc, annotation, isSelected, imageRect.Value);
            if (isSelected)
            {
                DrawHandles(dc, annotation, imageRect.Value);
            }
        }

        if (_previewAnnotation is not null)
        {
            DrawAnnotation(dc, _previewAnnotation, true, imageRect.Value);
            DrawHandles(dc, _previewAnnotation, imageRect.Value);
        }

        DrawPendingPolygon(dc, imageRect.Value);
    }

    protected override void OnMouseMove(MouseEventArgs e)
    {
        base.OnMouseMove(e);

        var viewportPoint = e.GetPosition(this);
        ViewportPointerMoved?.Invoke(this, new ViewportPointerEventArgs(viewportPoint));

        switch (_interactionMode)
        {
            case OverlayInteractionMode.Panning when _panOriginViewport is not null:
                PanOffsetX = _panOriginX + (viewportPoint.X - _panOriginViewport.Value.X);
                PanOffsetY = _panOriginY + (viewportPoint.Y - _panOriginViewport.Value.Y);
                RaiseTransformChanged();
                InvalidateVisual();
                return;
            case OverlayInteractionMode.None:
                return;
        }

        if (Image is null || _dragStartImage is null || _dragSeedAnnotation is null && _interactionMode != OverlayInteractionMode.Creating)
        {
            return;
        }

        if (!TryToImage(viewportPoint, out var imagePoint))
        {
            return;
        }

        _previewAnnotation = BuildPreview(imagePoint);
        InvalidateVisual();
    }

    protected override void OnMouseWheel(MouseWheelEventArgs e)
    {
        base.OnMouseWheel(e);
        ApplyZoom(e.Delta > 0 ? 1.1 : 1.0 / 1.1, e.GetPosition(this));
        e.Handled = true;
    }

    protected override void OnMouseLeftButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonDown(e);
        Focus();
        CaptureMouse();

        var viewportPoint = e.GetPosition(this);
        if (!TryToImage(viewportPoint, out var imagePoint))
        {
            ReleaseMouseCapture();
            return;
        }

        var selectedAnnotation = Annotations.FirstOrDefault(annotation => annotation.Id == SelectedAnnotationId);
        if (selectedAnnotation is not null && TryHitHandle(selectedAnnotation, viewportPoint, out var handleMode, out var vertexIndex))
        {
            if ((Keyboard.Modifiers & ModifierKeys.Alt) == ModifierKeys.Alt &&
                selectedAnnotation.Kind == AnnotationKind.Polygon &&
                vertexIndex >= 0 &&
                selectedAnnotation.Polygon is not null &&
                selectedAnnotation.Polygon.Count > 3)
            {
                AnnotationUpdated?.Invoke(this, new AnnotationRecordEventArgs(RemovePolygonVertex(selectedAnnotation, vertexIndex)));
                ReleaseMouseCapture();
                InvalidateVisual();
                return;
            }

            _interactionMode = handleMode;
            _activeVertexIndex = vertexIndex;
            _dragSeedAnnotation = selectedAnnotation;
            _dragStartViewport = viewportPoint;
            _dragStartImage = imagePoint;
            _previewAnnotation = selectedAnnotation;
            InvalidateVisual();
            return;
        }

        var hit = HitTestAnnotation(viewportPoint);
        if (hit is not null)
        {
            if ((Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control &&
                hit.Kind == AnnotationKind.Polygon &&
                TryHitPolygonEdge(hit, viewportPoint, out var insertIndex))
            {
                AnnotationUpdated?.Invoke(this, new AnnotationRecordEventArgs(InsertPolygonVertex(hit, insertIndex, imagePoint)));
                AnnotationSelectionChanged?.Invoke(this, new AnnotationSelectionEventArgs(hit.Id));
                ReleaseMouseCapture();
                InvalidateVisual();
                return;
            }

            _dragSeedAnnotation = hit;
            _dragStartViewport = viewportPoint;
            _dragStartImage = imagePoint;
            _interactionMode = hit.Kind switch
            {
                AnnotationKind.Point => OverlayInteractionMode.MovingPoint,
                _ => OverlayInteractionMode.Moving
            };
            AnnotationSelectionChanged?.Invoke(this, new AnnotationSelectionEventArgs(hit.Id));
            InvalidateVisual();
            return;
        }

        _dragStartViewport = viewportPoint;
        _dragStartImage = imagePoint;
        _dragSeedAnnotation = null;
        _previewAnnotation = null;
        _activeVertexIndex = -1;
        _interactionMode = OverlayInteractionMode.Creating;

        switch (Tool)
        {
            case AnnotationTool.Point:
                AnnotationCreated?.Invoke(this, new AnnotationRecordEventArgs(new AnnotationRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = AnnotationKind.Point,
                    Point = imagePoint
                }));
                _interactionMode = OverlayInteractionMode.None;
                ReleaseMouseCapture();
                break;
            case AnnotationTool.ImageRecognition:
                AnnotationCreated?.Invoke(this, new AnnotationRecordEventArgs(new AnnotationRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = AnnotationKind.ImageRecognition
                }));
                _interactionMode = OverlayInteractionMode.None;
                ReleaseMouseCapture();
                break;
            case AnnotationTool.Polygon:
                _pendingPolygon.Add(imagePoint);
                if (e.ClickCount >= 2)
                {
                    FinalizePendingPolygon();
                    _interactionMode = OverlayInteractionMode.None;
                    ReleaseMouseCapture();
                }
                break;
            default:
                break;
        }

        InvalidateVisual();
    }

    protected override void OnMouseLeftButtonUp(MouseButtonEventArgs e)
    {
        base.OnMouseLeftButtonUp(e);
        ReleaseMouseCapture();

        if (_previewAnnotation is not null)
        {
            if (_dragSeedAnnotation is not null && _interactionMode != OverlayInteractionMode.Creating)
            {
                AnnotationUpdated?.Invoke(this, new AnnotationRecordEventArgs(_previewAnnotation));
            }
            else
            {
                AnnotationCreated?.Invoke(this, new AnnotationRecordEventArgs(_previewAnnotation));
            }
        }

        _previewAnnotation = null;
        _dragStartViewport = null;
        _dragStartImage = null;
        _dragSeedAnnotation = null;
        _interactionMode = OverlayInteractionMode.None;
        _activeVertexIndex = -1;
        InvalidateVisual();
    }

    protected override void OnMouseRightButtonDown(MouseButtonEventArgs e)
    {
        base.OnMouseRightButtonDown(e);
        CancelPendingGeometry();
    }

    protected override void OnMouseDown(MouseButtonEventArgs e)
    {
        base.OnMouseDown(e);
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        Focus();
        CaptureMouse();
        _interactionMode = OverlayInteractionMode.Panning;
        _panOriginViewport = e.GetPosition(this);
        _panOriginX = PanOffsetX;
        _panOriginY = PanOffsetY;
        e.Handled = true;
    }

    protected override void OnMouseUp(MouseButtonEventArgs e)
    {
        base.OnMouseUp(e);
        if (e.ChangedButton != MouseButton.Middle)
        {
            return;
        }

        ReleaseMouseCapture();
        if (_interactionMode == OverlayInteractionMode.Panning)
        {
            _interactionMode = OverlayInteractionMode.None;
            _panOriginViewport = null;
        }
        e.Handled = true;
    }

    private void DrawPendingPolygon(DrawingContext dc, Rect imageRect)
    {
        if (_pendingPolygon.Count == 0)
        {
            return;
        }

        var geometry = new StreamGeometry();
        using var ctx = geometry.Open();
        var first = ToViewport(_pendingPolygon[0], imageRect);
        ctx.BeginFigure(first, false, false);
        foreach (var point in _pendingPolygon.Skip(1))
        {
            ctx.LineTo(ToViewport(point, imageRect), true, false);
        }

        geometry.Freeze();
        dc.DrawGeometry(null, _selectedPen, geometry);

        foreach (var point in _pendingPolygon)
        {
            dc.DrawEllipse(_pointBrush, _selectedPen, ToViewport(point, imageRect), 4, 4);
        }
    }

    private void DrawAnnotation(DrawingContext dc, AnnotationRecord annotation, bool isSelected, Rect imageRect)
    {
        var basePen = !string.IsNullOrWhiteSpace(annotation.SuggestedLabel) ? _suggestionPen : _shapePen;
        var fill = !string.IsNullOrWhiteSpace(annotation.SuggestedLabel) ? _suggestionFillBrush : _fillBrush;
        var pen = isSelected ? _selectedPen : basePen;
        switch (annotation.Kind)
        {
            case AnnotationKind.Rect when annotation.Rect is not null:
                dc.DrawRectangle(fill, pen, ToViewport(annotation.Rect.Value, imageRect));
                break;
            case AnnotationKind.Point when annotation.Point is not null:
                dc.DrawEllipse(_pointBrush, pen, ToViewport(annotation.Point.Value, imageRect), 5, 5);
                break;
            case AnnotationKind.Line when annotation.Line is not null:
                dc.DrawLine(pen, ToViewport(annotation.Line.Value.Start, imageRect), ToViewport(annotation.Line.Value.End, imageRect));
                break;
            case AnnotationKind.Polygon when annotation.Polygon is not null && annotation.Polygon.Count >= 2:
                var geometry = new StreamGeometry();
                using (var ctx = geometry.Open())
                {
                    ctx.BeginFigure(ToViewport(annotation.Polygon[0], imageRect), true, true);
                    foreach (var point in annotation.Polygon.Skip(1))
                    {
                        ctx.LineTo(ToViewport(point, imageRect), true, false);
                    }
                }
                geometry.Freeze();
                dc.DrawGeometry(fill, pen, geometry);
                break;
        }
    }

    private void DrawHandles(DrawingContext dc, AnnotationRecord annotation, Rect imageRect)
    {
        foreach (var handle in GetHandlePoints(annotation, imageRect))
        {
            dc.DrawRectangle(_handleBrush, _selectedPen, new Rect(handle.X - 4, handle.Y - 4, 8, 8));
        }
    }

    private IEnumerable<Point> GetHandlePoints(AnnotationRecord annotation, Rect imageRect)
    {
        switch (annotation.Kind)
        {
            case AnnotationKind.Rect when annotation.Rect is not null:
                var rect = ToViewport(annotation.Rect.Value, imageRect);
                return
                [
                    rect.TopLeft,
                    rect.TopRight,
                    rect.BottomLeft,
                    rect.BottomRight
                ];
            case AnnotationKind.Point when annotation.Point is not null:
                return [ToViewport(annotation.Point.Value, imageRect)];
            case AnnotationKind.Line when annotation.Line is not null:
                return [ToViewport(annotation.Line.Value.Start, imageRect), ToViewport(annotation.Line.Value.End, imageRect)];
            case AnnotationKind.Polygon when annotation.Polygon is not null:
                return annotation.Polygon.Select(point => ToViewport(point, imageRect)).ToArray();
            default:
                return Array.Empty<Point>();
        }
    }

    private bool TryHitHandle(AnnotationRecord annotation, Point viewportPoint, out OverlayInteractionMode mode, out int vertexIndex)
    {
        mode = OverlayInteractionMode.None;
        vertexIndex = -1;
        var imageRect = GetImageViewportRect();
        if (imageRect is null)
        {
            return false;
        }

        var handles = GetHandlePoints(annotation, imageRect.Value).ToArray();
        for (var index = 0; index < handles.Length; index++)
        {
            if ((handles[index] - viewportPoint).Length > 8)
            {
                continue;
            }

            mode = annotation.Kind switch
            {
                AnnotationKind.Rect => index switch
                {
                    0 => OverlayInteractionMode.ResizingRectTopLeft,
                    1 => OverlayInteractionMode.ResizingRectTopRight,
                    2 => OverlayInteractionMode.ResizingRectBottomLeft,
                    3 => OverlayInteractionMode.ResizingRectBottomRight,
                    _ => OverlayInteractionMode.None
                },
                AnnotationKind.Point => OverlayInteractionMode.MovingPoint,
                AnnotationKind.Line => index == 0 ? OverlayInteractionMode.MovingLineStart : OverlayInteractionMode.MovingLineEnd,
                AnnotationKind.Polygon => OverlayInteractionMode.MovingPolygonVertex,
                _ => OverlayInteractionMode.None
            };
            vertexIndex = index;
            return mode != OverlayInteractionMode.None;
        }

        return false;
    }

    private AnnotationRecord? HitTestAnnotation(Point viewportPoint)
    {
        var imageRect = GetImageViewportRect();
        if (imageRect is null)
        {
            return null;
        }

        foreach (var annotation in Annotations.Reverse())
        {
            if (!annotation.IsVisible)
            {
                continue;
            }

            switch (annotation.Kind)
            {
                case AnnotationKind.Rect when annotation.Rect is not null:
                    var rect = ToViewport(annotation.Rect.Value, imageRect.Value);
                    rect.Inflate(5, 5);
                    if (rect.Contains(viewportPoint))
                    {
                        return annotation;
                    }
                    break;
                case AnnotationKind.Point when annotation.Point is not null:
                    if ((ToViewport(annotation.Point.Value, imageRect.Value) - viewportPoint).Length <= 8)
                    {
                        return annotation;
                    }
                    break;
                case AnnotationKind.Line when annotation.Line is not null:
                    if (DistanceToSegment(viewportPoint, ToViewport(annotation.Line.Value.Start, imageRect.Value), ToViewport(annotation.Line.Value.End, imageRect.Value)) <= 8)
                    {
                        return annotation;
                    }
                    break;
                case AnnotationKind.Polygon when annotation.Polygon is not null && annotation.Polygon.Count >= 3:
                    var polygonPoints = annotation.Polygon.Select(point => ToViewport(point, imageRect.Value)).ToArray();
                    if (IsPointInPolygon(viewportPoint, polygonPoints) || IsPointNearPolygonEdge(viewportPoint, polygonPoints, 8))
                    {
                        return annotation;
                    }
                    break;
            }
        }

        AnnotationSelectionChanged?.Invoke(this, new AnnotationSelectionEventArgs(null));
        return null;
    }

    private AnnotationRecord? BuildPreview(Point2D imagePoint)
    {
        if (_interactionMode == OverlayInteractionMode.Creating && _dragStartImage is not null)
        {
            return Tool switch
            {
                AnnotationTool.Rect => BuildRectAnnotation(_dragStartImage.Value, imagePoint, null),
                AnnotationTool.Line => BuildLineAnnotation(_dragStartImage.Value, imagePoint, null),
                _ => null
            };
        }

        if (_dragSeedAnnotation is null || _dragStartImage is null)
        {
            return null;
        }

        var dx = imagePoint.X - _dragStartImage.Value.X;
        var dy = imagePoint.Y - _dragStartImage.Value.Y;

        return _interactionMode switch
        {
            OverlayInteractionMode.Moving => Translate(_dragSeedAnnotation, dx, dy),
            OverlayInteractionMode.MovingPoint => _dragSeedAnnotation with { Point = imagePoint },
            OverlayInteractionMode.MovingLineStart when _dragSeedAnnotation.Line is not null => _dragSeedAnnotation with
            {
                Line = new LineD(imagePoint, _dragSeedAnnotation.Line.Value.End)
            },
            OverlayInteractionMode.MovingLineEnd when _dragSeedAnnotation.Line is not null => _dragSeedAnnotation with
            {
                Line = new LineD(_dragSeedAnnotation.Line.Value.Start, imagePoint)
            },
            OverlayInteractionMode.MovingPolygonVertex when _dragSeedAnnotation.Polygon is not null && _activeVertexIndex >= 0 => UpdatePolygonVertex(_dragSeedAnnotation, _activeVertexIndex, imagePoint),
            OverlayInteractionMode.ResizingRectTopLeft or OverlayInteractionMode.ResizingRectTopRight or OverlayInteractionMode.ResizingRectBottomLeft or OverlayInteractionMode.ResizingRectBottomRight
                => ResizeRect(_dragSeedAnnotation, imagePoint, _interactionMode),
            _ => null
        };
    }

    private static AnnotationRecord UpdatePolygonVertex(AnnotationRecord annotation, int vertexIndex, Point2D imagePoint)
    {
        var points = annotation.Polygon!.ToArray();
        points[vertexIndex] = imagePoint;
        return annotation with { Polygon = points };
    }

    private static AnnotationRecord InsertPolygonVertex(AnnotationRecord annotation, int insertIndex, Point2D imagePoint)
    {
        var points = annotation.Polygon!.ToList();
        points.Insert(insertIndex, imagePoint);
        return annotation with { Polygon = points.ToArray() };
    }

    private static AnnotationRecord RemovePolygonVertex(AnnotationRecord annotation, int vertexIndex)
    {
        var points = annotation.Polygon!.ToList();
        points.RemoveAt(vertexIndex);
        return annotation with { Polygon = points.ToArray() };
    }

    private static AnnotationRecord? ResizeRect(AnnotationRecord annotation, Point2D imagePoint, OverlayInteractionMode mode)
    {
        if (annotation.Rect is null)
        {
            return null;
        }

        var rect = annotation.Rect.Value;
        var opposite = mode switch
        {
            OverlayInteractionMode.ResizingRectTopLeft => new Point2D(rect.X + rect.Width, rect.Y + rect.Height),
            OverlayInteractionMode.ResizingRectTopRight => new Point2D(rect.X, rect.Y + rect.Height),
            OverlayInteractionMode.ResizingRectBottomLeft => new Point2D(rect.X + rect.Width, rect.Y),
            OverlayInteractionMode.ResizingRectBottomRight => new Point2D(rect.X, rect.Y),
            _ => new Point2D(rect.X + rect.Width, rect.Y + rect.Height)
        };

        return annotation with
        {
            Rect = new RectD(
                Math.Min(imagePoint.X, opposite.X),
                Math.Min(imagePoint.Y, opposite.Y),
                Math.Abs(opposite.X - imagePoint.X),
                Math.Abs(opposite.Y - imagePoint.Y))
        };
    }

    private Rect? GetImageViewportRect()
    {
        if (Image?.PixelSize is null || ActualWidth <= 0 || ActualHeight <= 0)
        {
            return null;
        }

        var fitScale = Math.Min(ActualWidth / Image.PixelSize.Value.Width, ActualHeight / Image.PixelSize.Value.Height);
        var width = Image.PixelSize.Value.Width * fitScale * Zoom;
        var height = Image.PixelSize.Value.Height * fitScale * Zoom;
        return new Rect((ActualWidth - width) / 2.0 + PanOffsetX, (ActualHeight - height) / 2.0 + PanOffsetY, width, height);
    }

    private bool TryToImage(Point viewportPoint, out Point2D imagePoint)
    {
        imagePoint = default;
        var imageRect = GetImageViewportRect();
        if (imageRect is null || Image?.PixelSize is null || !imageRect.Value.Contains(viewportPoint))
        {
            return false;
        }

        var scaleX = Image.PixelSize.Value.Width / imageRect.Value.Width;
        var scaleY = Image.PixelSize.Value.Height / imageRect.Value.Height;
        imagePoint = new Point2D(
            (viewportPoint.X - imageRect.Value.X) * scaleX,
            (viewportPoint.Y - imageRect.Value.Y) * scaleY);
        return true;
    }

    private Point ToViewport(Point2D point, Rect imageRect, Size2D pixelSize)
    {
        return new Point(
            imageRect.X + point.X / pixelSize.Width * imageRect.Width,
            imageRect.Y + point.Y / pixelSize.Height * imageRect.Height);
    }

    private Point ToViewport(Point2D point, Rect imageRect)
    {
        if (Image?.PixelSize is null)
        {
            return new Point();
        }

        return ToViewport(point, imageRect, Image.PixelSize.Value);
    }

    private Rect ToViewport(RectD rect, Rect imageRect)
    {
        if (Image?.PixelSize is null)
        {
            return Rect.Empty;
        }

        var topLeft = ToViewport(new Point2D(rect.X, rect.Y), imageRect, Image.PixelSize.Value);
        var bottomRight = ToViewport(new Point2D(rect.X + rect.Width, rect.Y + rect.Height), imageRect, Image.PixelSize.Value);
        return new Rect(topLeft, bottomRight);
    }

    private void ApplyZoom(double factor, Point anchor)
    {
        if (Image?.PixelSize is null)
        {
            return;
        }

        var previousRect = GetImageViewportRect();
        Point2D? anchorImagePoint = null;
        if (previousRect is not null)
        {
            TryToImage(anchor, out var imagePoint);
            anchorImagePoint = imagePoint;
        }

        Zoom = Math.Clamp(Zoom * factor, 0.1, 12.0);

        if (anchorImagePoint is not null)
        {
            var newRect = GetImageViewportRect();
            if (newRect is not null)
            {
                var newAnchorPoint = ToViewport(anchorImagePoint.Value, newRect.Value);
                PanOffsetX += anchor.X - newAnchorPoint.X;
                PanOffsetY += anchor.Y - newAnchorPoint.Y;
            }
        }

        RaiseTransformChanged();
        InvalidateVisual();
    }

    private void RaiseTransformChanged()
    {
        ViewportTransformChanged?.Invoke(this, new ViewportTransformChangedEventArgs(Zoom, PanOffsetX, PanOffsetY));
    }

    private static AnnotationRecord BuildRectAnnotation(Point2D start, Point2D end, string? labelId)
    {
        return new AnnotationRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = AnnotationKind.Rect,
            LabelId = labelId,
            Rect = new RectD(
                Math.Min(start.X, end.X),
                Math.Min(start.Y, end.Y),
                Math.Abs(end.X - start.X),
                Math.Abs(end.Y - start.Y))
        };
    }

    private static AnnotationRecord BuildLineAnnotation(Point2D start, Point2D end, string? labelId)
    {
        return new AnnotationRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Kind = AnnotationKind.Line,
            LabelId = labelId,
            Line = new LineD(start, end)
        };
    }

    private static AnnotationRecord Translate(AnnotationRecord source, double dx, double dy)
    {
        return source.Kind switch
        {
            AnnotationKind.Rect when source.Rect is not null => source with
            {
                Rect = source.Rect.Value with { X = source.Rect.Value.X + dx, Y = source.Rect.Value.Y + dy }
            },
            AnnotationKind.Point when source.Point is not null => source with
            {
                Point = new Point2D(source.Point.Value.X + dx, source.Point.Value.Y + dy)
            },
            AnnotationKind.Line when source.Line is not null => source with
            {
                Line = new LineD(
                    new Point2D(source.Line.Value.Start.X + dx, source.Line.Value.Start.Y + dy),
                    new Point2D(source.Line.Value.End.X + dx, source.Line.Value.End.Y + dy))
            },
            AnnotationKind.Polygon when source.Polygon is not null => source with
            {
                Polygon = source.Polygon.Select(point => new Point2D(point.X + dx, point.Y + dy)).ToArray()
            },
            _ => source
        };
    }

    private static double DistanceToSegment(Point point, Point start, Point end)
    {
        var dx = end.X - start.X;
        var dy = end.Y - start.Y;
        if (dx == 0 && dy == 0)
        {
            return (point - start).Length;
        }

        var t = ((point.X - start.X) * dx + (point.Y - start.Y) * dy) / (dx * dx + dy * dy);
        t = Math.Clamp(t, 0, 1);
        var projection = new Point(start.X + t * dx, start.Y + t * dy);
        return (point - projection).Length;
    }

    private bool TryHitPolygonEdge(AnnotationRecord annotation, Point viewportPoint, out int insertIndex)
    {
        insertIndex = -1;
        var imageRect = GetImageViewportRect();
        if (imageRect is null || annotation.Polygon is null || annotation.Polygon.Count < 3)
        {
            return false;
        }

        var viewportPolygon = annotation.Polygon.Select(point => ToViewport(point, imageRect.Value)).ToArray();
        var bestDistance = double.MaxValue;
        for (var index = 0; index < viewportPolygon.Length; index++)
        {
            var nextIndex = (index + 1) % viewportPolygon.Length;
            var distance = DistanceToSegment(viewportPoint, viewportPolygon[index], viewportPolygon[nextIndex]);
            if (distance > 10 || distance >= bestDistance)
            {
                continue;
            }

            bestDistance = distance;
            insertIndex = nextIndex;
        }

        return insertIndex >= 0;
    }

    private static bool IsPointNearPolygonEdge(Point viewportPoint, IReadOnlyList<Point> polygon, double threshold)
    {
        for (var index = 0; index < polygon.Count; index++)
        {
            var nextIndex = (index + 1) % polygon.Count;
            if (DistanceToSegment(viewportPoint, polygon[index], polygon[nextIndex]) <= threshold)
            {
                return true;
            }
        }

        return false;
    }

    private static bool IsPointInPolygon(Point point, IReadOnlyList<Point> polygon)
    {
        var inside = false;
        for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
        {
            if (((polygon[i].Y > point.Y) != (polygon[j].Y > point.Y)) &&
                (point.X < (polygon[j].X - polygon[i].X) * (point.Y - polygon[i].Y) / (polygon[j].Y - polygon[i].Y + double.Epsilon) + polygon[i].X))
            {
                inside = !inside;
            }
        }

        return inside;
    }
}

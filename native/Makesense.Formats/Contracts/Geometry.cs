namespace Makesense.Formats.Contracts;

public enum AnnotationKind
{
    Rect,
    Point,
    Line,
    Polygon,
    ImageRecognition
}

public enum AnnotationTool
{
    Rect,
    Point,
    Line,
    Polygon,
    ImageRecognition
}

public enum ProjectKind
{
    ObjectDetection,
    ImageRecognition
}

public enum AnnotationFormat
{
    Csv,
    Yolo,
    Voc,
    Vgg,
    Coco,
    Json,
    YoloImageTxt
}

public readonly record struct Size2D(double Width, double Height);

public readonly record struct Point2D(double X, double Y);

public readonly record struct RectD(double X, double Y, double Width, double Height);

public readonly record struct LineD(Point2D Start, Point2D End);

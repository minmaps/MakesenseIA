namespace Makesense.Formats.Contracts;

public static class FormatSupport
{
    public static IReadOnlyList<AnnotationFormat> GetImportFormats(AnnotationKind kind)
    {
        return kind switch
        {
            AnnotationKind.Rect => [AnnotationFormat.Coco, AnnotationFormat.Yolo, AnnotationFormat.Voc],
            AnnotationKind.Polygon => [AnnotationFormat.Coco],
            _ => []
        };
    }

    public static IReadOnlyList<AnnotationFormat> GetExportFormats(AnnotationKind kind)
    {
        return kind switch
        {
            AnnotationKind.Rect => [AnnotationFormat.Yolo, AnnotationFormat.Voc, AnnotationFormat.Csv],
            AnnotationKind.Point => [AnnotationFormat.Csv],
            AnnotationKind.Line => [AnnotationFormat.Csv],
            AnnotationKind.Polygon => [AnnotationFormat.Vgg, AnnotationFormat.Coco],
            AnnotationKind.ImageRecognition => [AnnotationFormat.Csv, AnnotationFormat.Json],
            _ => []
        };
    }
}

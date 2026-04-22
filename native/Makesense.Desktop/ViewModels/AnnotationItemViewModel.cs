using Makesense.Formats.Contracts;

namespace Makesense.Desktop.ViewModels;

public sealed class AnnotationItemViewModel
{
    public AnnotationItemViewModel(AnnotationRecord record, IReadOnlyDictionary<string, string> labelNames, bool showConfidence)
    {
        Record = record;
        Summary = BuildSummary(record, labelNames, showConfidence);
    }

    public AnnotationRecord Record { get; }

    public string Summary { get; }

    private static string BuildSummary(AnnotationRecord record, IReadOnlyDictionary<string, string> labelNames, bool showConfidence)
    {
        var label = record.LabelId is not null && labelNames.TryGetValue(record.LabelId, out var name)
            ? name
            : "unlabeled";
        var confidenceSuffix = showConfidence && record.SuggestedConfidence is double confidence
            ? $" ({ToConfidencePercent(confidence):0}%)"
            : string.Empty;
        var suggestionPrefix = string.IsNullOrWhiteSpace(record.SuggestedLabel)
            ? string.Empty
            : $"Suggested:{record.SuggestedLabel}{confidenceSuffix} | ";

        return record.Kind switch
        {
            AnnotationKind.Rect when record.Rect is not null =>
                $"{suggestionPrefix}Rect [{label}] x={record.Rect.Value.X:0}, y={record.Rect.Value.Y:0}, w={record.Rect.Value.Width:0}, h={record.Rect.Value.Height:0}",
            AnnotationKind.Point when record.Point is not null =>
                $"{suggestionPrefix}Point [{label}] x={record.Point.Value.X:0}, y={record.Point.Value.Y:0}",
            AnnotationKind.Line when record.Line is not null =>
                $"{suggestionPrefix}Line [{label}] ({record.Line.Value.Start.X:0},{record.Line.Value.Start.Y:0}) -> ({record.Line.Value.End.X:0},{record.Line.Value.End.Y:0})",
            AnnotationKind.Polygon when record.Polygon is not null =>
                $"{suggestionPrefix}Polygon [{label}] {record.Polygon.Count} points",
            AnnotationKind.ImageRecognition =>
                $"{suggestionPrefix}Image label [{label}]",
            _ => $"{suggestionPrefix}{record.Kind} [{label}]"
        };
    }

    private static double ToConfidencePercent(double confidence)
    {
        var percent = confidence <= 1.0 ? confidence * 100.0 : confidence;
        return Math.Clamp(percent, 0.0, 100.0);
    }
}

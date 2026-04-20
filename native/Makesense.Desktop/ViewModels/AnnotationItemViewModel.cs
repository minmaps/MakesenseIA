using Makesense.Formats.Contracts;

namespace Makesense.Desktop.ViewModels;

public sealed class AnnotationItemViewModel
{
    public AnnotationItemViewModel(AnnotationRecord record, IReadOnlyDictionary<string, string> labelNames)
    {
        Record = record;
        Summary = BuildSummary(record, labelNames);
    }

    public AnnotationRecord Record { get; }

    public string Summary { get; }

    private static string BuildSummary(AnnotationRecord record, IReadOnlyDictionary<string, string> labelNames)
    {
        var label = record.LabelId is not null && labelNames.TryGetValue(record.LabelId, out var name)
            ? name
            : "unlabeled";
        var suggestionPrefix = string.IsNullOrWhiteSpace(record.SuggestedLabel)
            ? string.Empty
            : $"Suggested:{record.SuggestedLabel} | ";

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
}

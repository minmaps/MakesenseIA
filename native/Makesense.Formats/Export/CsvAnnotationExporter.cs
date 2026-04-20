using System.Globalization;
using System.Text;
using Makesense.Formats.Contracts;

namespace Makesense.Formats.Export;

public static class CsvAnnotationExporter
{
    public static string Export(ProjectState state)
    {
        var builder = new StringBuilder();
        builder.AppendLine("image_id,image_name,annotation_id,kind,label_id,x,y,width,height,x2,y2,points");

        foreach (var image in state.Images)
        {
            foreach (var annotation in image.Annotations)
            {
                var row = new[]
                {
                    Escape(image.Id),
                    Escape(image.FileName),
                    Escape(annotation.Id),
                    Escape(annotation.Kind.ToString()),
                    Escape(annotation.LabelId ?? string.Empty),
                    Format(annotation.Rect?.X ?? annotation.Point?.X ?? annotation.Line?.Start.X),
                    Format(annotation.Rect?.Y ?? annotation.Point?.Y ?? annotation.Line?.Start.Y),
                    Format(annotation.Rect?.Width),
                    Format(annotation.Rect?.Height),
                    Format(annotation.Line?.End.X),
                    Format(annotation.Line?.End.Y),
                    Escape(annotation.Polygon is null
                        ? string.Empty
                        : string.Join(";", annotation.Polygon.Select(point => $"{Format(point.X)}:{Format(point.Y)}")))
                };

                builder.AppendLine(string.Join(",", row));
            }
        }

        return builder.ToString();
    }

    private static string Format(double? value)
    {
        return value?.ToString("0.###", CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private static string Escape(string value)
    {
        return $"\"{value.Replace("\"", "\"\"", StringComparison.Ordinal)}\"";
    }
}

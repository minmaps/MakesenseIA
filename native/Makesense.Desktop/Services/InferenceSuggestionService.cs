using System.IO;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.Services;

public sealed class InferenceSuggestionService
{
    public IReadOnlyList<AnnotationRecord> GenerateSuggestions(
        ImageRecord image,
        string taskName,
        string modelPath,
        IReadOnlyList<LabelClass> labels,
        IReadOnlyCollection<AnnotationRecord> existingAnnotations)
    {
        if (image.PixelSize is null)
        {
            return Array.Empty<AnnotationRecord>();
        }

        var seed = HashCode.Combine(image.Path.ToLowerInvariant(), taskName.ToLowerInvariant(), modelPath.ToLowerInvariant());
        var labelName = ResolveSuggestedLabel(taskName, modelPath, labels);
        var takenRects = existingAnnotations.Where(annotation => annotation.Rect is not null).Select(annotation => annotation.Rect!.Value).ToArray();
        var width = image.PixelSize.Value.Width;
        var height = image.PixelSize.Value.Height;

        return taskName switch
        {
            "pose-estimation" => BuildPoseSuggestions(width, height, seed, labelName),
            _ => BuildRectSuggestions(width, height, seed, labelName, takenRects)
        };
    }

    private static IReadOnlyList<AnnotationRecord> BuildRectSuggestions(
        double imageWidth,
        double imageHeight,
        int seed,
        string suggestedLabel,
        IReadOnlyList<RectD> takenRects)
    {
        var random = new Random(seed);
        var suggestions = new List<AnnotationRecord>(3);
        var desiredWidth = Math.Clamp(imageWidth * 0.22, 48, imageWidth * 0.45);
        var desiredHeight = Math.Clamp(imageHeight * 0.18, 48, imageHeight * 0.45);

        for (var attempt = 0; attempt < 12 && suggestions.Count < 3; attempt++)
        {
            var x = random.NextDouble() * Math.Max(1, imageWidth - desiredWidth);
            var y = random.NextDouble() * Math.Max(1, imageHeight - desiredHeight);
            var rect = new RectD(x, y, desiredWidth, desiredHeight);

            if (takenRects.Any(existing => IntersectionOverUnion(existing, rect) > 0.25) ||
                suggestions.Any(existing => existing.Rect is not null && IntersectionOverUnion(existing.Rect.Value, rect) > 0.15))
            {
                continue;
            }

            suggestions.Add(new AnnotationRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = AnnotationKind.Rect,
                IsVisible = true,
                Rect = rect,
                SuggestedLabel = suggestedLabel
            });
        }

        return suggestions;
    }

    private static IReadOnlyList<AnnotationRecord> BuildPoseSuggestions(double imageWidth, double imageHeight, int seed, string suggestedLabel)
    {
        var random = new Random(seed);
        var points = new List<AnnotationRecord>(5);
        var centerX = imageWidth * (0.35 + random.NextDouble() * 0.3);
        var centerY = imageHeight * (0.25 + random.NextDouble() * 0.35);
        var spread = Math.Min(imageWidth, imageHeight) * 0.08;

        for (var index = 0; index < 5; index++)
        {
            var x = centerX + (index - 2) * spread * 0.55;
            var y = centerY + ((index % 2 == 0) ? -spread : spread);
            points.Add(new AnnotationRecord
            {
                Id = Guid.NewGuid().ToString("N"),
                Kind = AnnotationKind.Point,
                IsVisible = true,
                Point = new Point2D(Math.Clamp(x, 0, imageWidth), Math.Clamp(y, 0, imageHeight)),
                SuggestedLabel = suggestedLabel
            });
        }

        return points;
    }

    private static string ResolveSuggestedLabel(string taskName, string modelPath, IReadOnlyList<LabelClass> labels)
    {
        var modelName = Path.GetFileNameWithoutExtension(modelPath);
        if (!string.IsNullOrWhiteSpace(modelName))
        {
            var matchingLabel = labels.FirstOrDefault(label =>
                modelName.Contains(label.Name, StringComparison.OrdinalIgnoreCase));
            if (matchingLabel is not null)
            {
                return matchingLabel.Name;
            }
        }

        if (labels.Count == 1)
        {
            return labels[0].Name;
        }

        return taskName switch
        {
            "pose-estimation" => "pose",
            _ when !string.IsNullOrWhiteSpace(modelName) => modelName,
            _ => "object"
        };
    }

    private static double IntersectionOverUnion(RectD first, RectD second)
    {
        var x1 = Math.Max(first.X, second.X);
        var y1 = Math.Max(first.Y, second.Y);
        var x2 = Math.Min(first.X + first.Width, second.X + second.Width);
        var y2 = Math.Min(first.Y + first.Height, second.Y + second.Height);
        var intersectionWidth = Math.Max(0, x2 - x1);
        var intersectionHeight = Math.Max(0, y2 - y1);
        var intersection = intersectionWidth * intersectionHeight;
        if (intersection <= 0)
        {
            return 0;
        }

        var union = first.Width * first.Height + second.Width * second.Height - intersection;
        return union <= 0 ? 0 : intersection / union;
    }
}

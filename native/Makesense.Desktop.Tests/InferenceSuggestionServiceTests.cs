using Makesense.Desktop.Services;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.Tests;

public sealed class InferenceSuggestionServiceTests
{
    [Fact]
    public void GenerateSuggestions_CreatesRectSuggestionsForDetection()
    {
        var service = new InferenceSuggestionService();
        var image = new ImageRecord
        {
            Id = "img-1",
            Path = @"C:\images\sample.png",
            FileName = "sample.png",
            FileSizeBytes = 12,
            PixelSize = new Size2D(1920, 1080)
        };
        var labels = new[]
        {
            new LabelClass
            {
                Id = "label-1",
                Name = "car"
            }
        };

        var suggestions = service.GenerateSuggestions(image, "rect-detection", @"C:\models\detector.onnx", labels, Array.Empty<AnnotationRecord>());

        Assert.NotEmpty(suggestions);
        Assert.All(suggestions, suggestion =>
        {
            Assert.Equal(AnnotationKind.Rect, suggestion.Kind);
            Assert.Equal("car", suggestion.SuggestedLabel);
            Assert.NotNull(suggestion.Rect);
        });
    }

    [Fact]
    public void GenerateSuggestions_CreatesPointSuggestionsForPose()
    {
        var service = new InferenceSuggestionService();
        var image = new ImageRecord
        {
            Id = "img-2",
            Path = @"C:\images\pose.png",
            FileName = "pose.png",
            FileSizeBytes = 12,
            PixelSize = new Size2D(800, 600)
        };

        var suggestions = service.GenerateSuggestions(image, "pose-estimation", @"C:\models\pose.onnx", Array.Empty<LabelClass>(), Array.Empty<AnnotationRecord>());

        Assert.Equal(5, suggestions.Count);
        Assert.All(suggestions, suggestion =>
        {
            Assert.Equal(AnnotationKind.Point, suggestion.Kind);
            Assert.Equal("pose", suggestion.SuggestedLabel);
            Assert.NotNull(suggestion.Point);
        });
    }
}

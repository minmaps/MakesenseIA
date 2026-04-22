using System.Text.Json.Serialization;

namespace Makesense.Formats.Contracts;

public sealed record AnnotationRecord
{
    public required string Id { get; init; }

    public required AnnotationKind Kind { get; init; }

    public string? LabelId { get; init; }

    public bool IsVisible { get; init; } = true;

    public RectD? Rect { get; init; }

    public Point2D? Point { get; init; }

    public LineD? Line { get; init; }

    public IReadOnlyList<Point2D>? Polygon { get; init; }

    public string? SuggestedLabel { get; init; }

    public double? SuggestedConfidence { get; init; }
}

public sealed record LabelClass
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public string Color { get; init; } = "#4FD1C5";
}

public sealed record ImageRecord
{
    public required string Id { get; init; }

    public required string Path { get; init; }

    public required string FileName { get; init; }

    public long FileSizeBytes { get; init; }

    public Size2D? PixelSize { get; init; }

    public IReadOnlyList<AnnotationRecord> Annotations { get; init; } = Array.Empty<AnnotationRecord>();
}

public sealed record InferenceModelDescriptor
{
    public required string Name { get; init; }

    public required string Path { get; init; }

    public required string Task { get; init; }

    public string Backend { get; init; } = "TensorRT";

    public string Version { get; init; } = "v1";
}

public sealed record PerformanceConfig
{
    public uint MaxRamMb { get; init; }

    public uint MaxVramMb { get; init; }

    public uint MaxDecodeThreads { get; init; }

    public uint MaxIoThreads { get; init; }

    public uint MaxInferenceJobs { get; init; }

    public uint MaxPrefetchImages { get; init; }

    [JsonIgnore]
    public long MaxRamBytes => MaxRamMb * 1024L * 1024L;

    [JsonIgnore]
    public long MaxVramBytes => MaxVramMb * 1024L * 1024L;

    public PerformanceConfig Normalize()
    {
        return this with
        {
            MaxRamMb = Math.Max(512U, MaxRamMb),
            MaxVramMb = Math.Max(256U, MaxVramMb),
            MaxDecodeThreads = Math.Clamp(MaxDecodeThreads, 1U, 32U),
            MaxIoThreads = Math.Clamp(MaxIoThreads, 1U, 32U),
            MaxInferenceJobs = Math.Clamp(MaxInferenceJobs, 1U, 16U),
            MaxPrefetchImages = Math.Clamp(MaxPrefetchImages, 0U, 4096U)
        };
    }
}

public sealed record ProjectState
{
    public required string Name { get; init; }

    public required ProjectKind ProjectKind { get; init; }

    public string? ProjectPath { get; init; }

    public PerformanceConfig PerformanceConfig { get; init; } = new();

    public IReadOnlyList<LabelClass> Labels { get; init; } = Array.Empty<LabelClass>();

    public IReadOnlyList<ImageRecord> Images { get; init; } = Array.Empty<ImageRecord>();

    public InferenceModelDescriptor? ActiveModel { get; init; }

    public string? ActiveImageId { get; init; }

    public string? ActiveLabelId { get; init; }

    public string? ActiveAnnotationId { get; init; }

    public AnnotationTool SelectedTool { get; init; } = AnnotationTool.Rect;

    public AnnotationFormat? SelectedImportFormat { get; init; }

    public AnnotationFormat? SelectedExportFormat { get; init; }

    public double ViewZoom { get; init; } = 1.0;

    public double PanOffsetX { get; init; }

    public double PanOffsetY { get; init; }
}

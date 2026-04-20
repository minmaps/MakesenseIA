using Makesense.Formats.Contracts;
using Makesense.Formats.Export;
using Makesense.Formats.Project;
using Makesense.Formats.Serialization;

namespace Makesense.Desktop.Tests;

public sealed class PerformanceAndSerializationTests
{
    [Fact]
    public void PerformanceConfig_Normalize_ClampsUnsafeValues()
    {
        var config = new PerformanceConfig
        {
            MaxRamMb = 32,
            MaxVramMb = 64,
            MaxDecodeThreads = 0,
            MaxIoThreads = 200,
            MaxInferenceJobs = 0,
            MaxPrefetchImages = 99999
        };

        var normalized = config.Normalize();

        Assert.Equal<uint>(512, normalized.MaxRamMb);
        Assert.Equal<uint>(256, normalized.MaxVramMb);
        Assert.Equal<uint>(1, normalized.MaxDecodeThreads);
        Assert.Equal<uint>(32, normalized.MaxIoThreads);
        Assert.Equal<uint>(1, normalized.MaxInferenceJobs);
        Assert.Equal<uint>(4096, normalized.MaxPrefetchImages);
    }

    [Fact]
    public async Task ProjectStateSerializer_RoundTripsCoreFields()
    {
        var state = new ProjectState
        {
            Name = "sample",
            ProjectKind = ProjectKind.ObjectDetection,
            ProjectPath = @"C:\datasets\sample.msproj",
            PerformanceConfig = new PerformanceConfig
            {
                MaxRamMb = 1024,
                MaxVramMb = 2048,
                MaxDecodeThreads = 4,
                MaxIoThreads = 2,
                MaxInferenceJobs = 1,
                MaxPrefetchImages = 32
            },
            Images =
            [
                new ImageRecord
                {
                    Id = "img-1",
                    Path = @"C:\images\a.png",
                    FileName = "a.png",
                    FileSizeBytes = 1234,
                    Annotations =
                    [
                        new AnnotationRecord
                        {
                            Id = "ann-1",
                            Kind = AnnotationKind.Rect,
                            Rect = new RectD(1, 2, 3, 4),
                            IsVisible = false,
                            SuggestedLabel = "vehicle"
                        }
                    ]
                }
            ]
        };

        var path = Path.GetTempFileName();

        try
        {
            await ProjectStateSerializer.SaveAsync(state, path);
            var reloaded = await ProjectStateSerializer.LoadAsync(path);

            Assert.NotNull(reloaded);
            Assert.Equal(state.Name, reloaded!.Name);
            Assert.Single(reloaded.Images);
            Assert.Single(reloaded.Images[0].Annotations);
            Assert.Equal(AnnotationKind.Rect, reloaded.Images[0].Annotations[0].Kind);
            Assert.False(reloaded.Images[0].Annotations[0].IsVisible);
            Assert.Equal("vehicle", reloaded.Images[0].Annotations[0].SuggestedLabel);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void CsvExporter_ExportsAnnotationRows()
    {
        var state = new ProjectState
        {
            Name = "sample",
            ProjectKind = ProjectKind.ObjectDetection,
            Images =
            [
                new ImageRecord
                {
                    Id = "img-1",
                    Path = @"C:\images\a.png",
                    FileName = "a.png",
                    FileSizeBytes = 1234,
                    Annotations =
                    [
                        new AnnotationRecord
                        {
                            Id = "ann-1",
                            Kind = AnnotationKind.Rect,
                            LabelId = "label-1",
                            Rect = new RectD(10, 20, 30, 40)
                        }
                    ]
                }
            ]
        };

        var csv = CsvAnnotationExporter.Export(state);

        Assert.Contains("image_id,image_name,annotation_id", csv, StringComparison.Ordinal);
        Assert.Contains("\"img-1\",\"a.png\",\"ann-1\",\"Rect\",\"label-1\",10,20,30,40", csv, StringComparison.Ordinal);
    }

    [Fact]
    public void ProjectStateExtensions_SetActiveImage_PersistsSelection()
    {
        var state = new ProjectState
        {
            Name = "sample",
            ProjectKind = ProjectKind.ObjectDetection,
            Images =
            [
                new ImageRecord
                {
                    Id = "img-1",
                    Path = @"C:\images\a.png",
                    FileName = "a.png",
                    FileSizeBytes = 1234
                }
            ]
        };

        var updated = state.SetActiveImage("img-1");

        Assert.Equal("img-1", updated.ActiveImageId);
        Assert.Equal("img-1", updated.GetActiveImage()!.Id);
    }
}

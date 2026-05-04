using Makesense.Formats.Contracts;
using Makesense.Formats.Export;
using Makesense.Formats.Project;
using Makesense.Formats.Serialization;
using Makesense.Desktop.Interop;
using Makesense.Desktop.Services;
using Makesense.Desktop.ViewModels;
using System.Reflection;

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

    [Fact]
    public void PerformanceMonitorViewModel_Load_PublishesRealtimeProcessMetrics()
    {
        var viewModel = new PerformanceMonitorViewModel();

        viewModel.Load(new PerformanceMonitorSnapshot(
            RamUsedMb: 512.4,
            VramUsedMb: 128.8,
            CpuUsagePercent: 36.5,
            GpuUsagePercent: 41.2,
            GpuMetricsAvailable: true,
            CapturedAt: DateTimeOffset.UtcNow));

        Assert.Equal(512.4, viewModel.RamUsedMb);
        Assert.Equal(128.8, viewModel.VramUsedMb);
        Assert.Equal(36.5, viewModel.CpuUsagePercent);
        Assert.Equal(41.2, viewModel.GpuUsagePercent);
        Assert.True(viewModel.GpuMetricsAvailable);
        Assert.Contains("temps réel", viewModel.StatusText, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ImageMetadataService_ReadsRealRepositoryAsset()
    {
        var workspaceRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", ".."));
        var imagePath = Path.Combine(workspaceRoot, "public", "ico", "main-image-color.png");
        Assert.True(File.Exists(imagePath), $"Expected repo asset at {imagePath}.");

        var service = new ImageMetadataService();
        var record = service.CreateRecord(imagePath);

        Assert.Equal("main-image-color.png", record.FileName);
        Assert.True(record.FileSizeBytes > 0);
        Assert.NotNull(record.PixelSize);
        Assert.True(record.PixelSize!.Value.Width > 0);
        Assert.True(record.PixelSize!.Value.Height > 0);
    }

    [Fact]
    public async Task ProjectStateSerializer_RoundTripsActiveModelMetadata()
    {
        var state = new ProjectState
        {
            Name = "sample",
            ProjectKind = ProjectKind.ObjectDetection,
            ActiveModel = new InferenceModelDescriptor
            {
                Name = "detector",
                Path = @"C:\models\detector.onnx",
                Task = "rect-detection",
                Backend = "CUDA",
                Version = "v2"
            }
        };

        var path = Path.GetTempFileName();

        try
        {
            await ProjectStateSerializer.SaveAsync(state, path);
            var reloaded = await ProjectStateSerializer.LoadAsync(path);

            Assert.NotNull(reloaded);
            Assert.NotNull(reloaded!.ActiveModel);
            Assert.Equal("detector", reloaded.ActiveModel!.Name);
            Assert.Equal("CUDA", reloaded.ActiveModel.Backend);
            Assert.Equal("v2", reloaded.ActiveModel.Version);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public async Task ProjectStateSerializer_RoundTripsResumeWorkspaceState()
    {
        var state = new ProjectState
        {
            Name = "sample",
            ProjectKind = ProjectKind.ObjectDetection,
            ProjectPath = @"C:\datasets\sample.msproj",
            ActiveImageId = "img-1",
            ActiveLabelId = "label-1",
            ActiveAnnotationId = "ann-1",
            SelectedTool = AnnotationTool.Polygon,
            SelectedImportFormat = AnnotationFormat.Coco,
            SelectedExportFormat = AnnotationFormat.Vgg,
            ViewZoom = 2.25,
            PanOffsetX = 42,
            PanOffsetY = -18,
            Labels =
            [
                new LabelClass
                {
                    Id = "label-1",
                    Name = "vehicle"
                }
            ],
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
                            Kind = AnnotationKind.Polygon,
                            LabelId = "label-1",
                            Polygon =
                            [
                                new Point2D(0, 0),
                                new Point2D(10, 0),
                                new Point2D(10, 10)
                            ]
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
            Assert.Equal("img-1", reloaded!.ActiveImageId);
            Assert.Equal("label-1", reloaded.ActiveLabelId);
            Assert.Equal("ann-1", reloaded.ActiveAnnotationId);
            Assert.Equal(AnnotationTool.Polygon, reloaded.SelectedTool);
            Assert.Equal(AnnotationFormat.Coco, reloaded.SelectedImportFormat);
            Assert.Equal(AnnotationFormat.Vgg, reloaded.SelectedExportFormat);
            Assert.Equal(2.25, reloaded.ViewZoom);
            Assert.Equal(42, reloaded.PanOffsetX);
            Assert.Equal(-18, reloaded.PanOffsetY);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void MainWindowViewModel_RunActiveModelCommand_RequiresImagesModelPathAndLabel()
    {
        var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(settingsDirectory, "settings.json");
        Directory.CreateDirectory(settingsDirectory);

        try
        {
            var performanceService = new PerformanceConfigService(new HardwareInfoService(), settingsPath);
            using var session = new NativeEngineSession();
            using var viewModel = new MainWindowViewModel(performanceService, session, new PerformanceConfig
            {
                MaxRamMb = 1024,
                MaxVramMb = 1024,
                MaxDecodeThreads = 2,
                MaxIoThreads = 2,
                MaxInferenceJobs = 1,
                MaxPrefetchImages = 8
            });

            Assert.False(viewModel.RunActiveModelCommand.CanExecute(null));

            viewModel.ActiveModelPath = @"C:\models\detector.onnx";

            Assert.False(viewModel.RunActiveModelCommand.CanExecute(null));

            var replaceState = typeof(MainWindowViewModel).GetMethod("ReplaceState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(replaceState);
            replaceState!.Invoke(viewModel, [new ProjectState
            {
                Name = "sample",
                ProjectKind = ProjectKind.ObjectDetection,
                PerformanceConfig = new PerformanceConfig
                {
                    MaxRamMb = 1024,
                    MaxVramMb = 1024,
                    MaxDecodeThreads = 2,
                    MaxIoThreads = 2,
                    MaxInferenceJobs = 1,
                    MaxPrefetchImages = 8
                },
                Images =
                [
                    new ImageRecord
                    {
                        Id = "img-1",
                        Path = @"C:\images\a.png",
                        FileName = "a.png",
                        FileSizeBytes = 1234,
                        PixelSize = new Size2D(800, 600)
                    }
                ]
            }]);

            Assert.False(viewModel.RunActiveModelCommand.CanExecute(null));

            replaceState!.Invoke(viewModel, [new ProjectState
            {
                Name = "sample",
                ProjectKind = ProjectKind.ObjectDetection,
                PerformanceConfig = new PerformanceConfig
                {
                    MaxRamMb = 1024,
                    MaxVramMb = 1024,
                    MaxDecodeThreads = 2,
                    MaxIoThreads = 2,
                    MaxInferenceJobs = 1,
                    MaxPrefetchImages = 8
                },
                Labels =
                [
                    new LabelClass
                    {
                        Id = "label-1",
                        Name = "target"
                    }
                ],
                Images =
                [
                    new ImageRecord
                    {
                        Id = "img-1",
                        Path = @"C:\images\a.png",
                        FileName = "a.png",
                        FileSizeBytes = 1234,
                        PixelSize = new Size2D(800, 600)
                    }
                ]
            }]);

            Assert.True(viewModel.RunActiveModelCommand.CanExecute(null));
        }
        finally
        {
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        }
    }

    [Fact]
    public void MainWindowViewModel_FilterChangeSelectsNearestImageInsteadOfFirst()
    {
        var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(settingsDirectory, "settings.json");
        Directory.CreateDirectory(settingsDirectory);

        try
        {
            var performanceService = new PerformanceConfigService(new HardwareInfoService(), settingsPath);
            using var session = new NativeEngineSession();
            using var viewModel = new MainWindowViewModel(performanceService, session, new PerformanceConfig
            {
                MaxRamMb = 1024,
                MaxVramMb = 1024,
                MaxDecodeThreads = 2,
                MaxIoThreads = 2,
                MaxInferenceJobs = 1,
                MaxPrefetchImages = 8
            });
            var replaceState = typeof(MainWindowViewModel).GetMethod("ReplaceState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(replaceState);

            replaceState!.Invoke(viewModel, [new ProjectState
            {
                Name = "sample",
                ProjectKind = ProjectKind.ObjectDetection,
                ActiveImageId = "img-5",
                PerformanceConfig = new PerformanceConfig
                {
                    MaxRamMb = 1024,
                    MaxVramMb = 1024,
                    MaxDecodeThreads = 2,
                    MaxIoThreads = 2,
                    MaxInferenceJobs = 1,
                    MaxPrefetchImages = 8
                },
                Images =
                [
                    CreateImage("img-1", "a.png", hasSuggestion: true),
                    CreateImage("img-2", "b.png", hasSuggestion: false),
                    CreateImage("img-3", "c.png", hasSuggestion: true),
                    CreateImage("img-4", "d.png", hasSuggestion: false),
                    CreateImage("img-5", "e.png", hasSuggestion: false),
                    CreateImage("img-6", "f.png", hasSuggestion: true)
                ]
            }]);

            Assert.Equal("img-5", viewModel.SelectedImage?.Id);

            viewModel.SelectedImageFilter = ImageFilterMode.WithSuggestions;

            Assert.Equal("img-6", viewModel.SelectedImage?.Id);
        }
        finally
        {
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        }

        static ImageRecord CreateImage(string id, string fileName, bool hasSuggestion)
        {
            return new ImageRecord
            {
                Id = id,
                Path = $@"C:\images\{fileName}",
                FileName = fileName,
                FileSizeBytes = 1234,
                PixelSize = new Size2D(800, 600),
                Annotations = hasSuggestion
                    ?
                    [
                        new AnnotationRecord
                        {
                            Id = $"ann-{id}",
                            Kind = AnnotationKind.Rect,
                            SuggestedLabel = "Enemy",
                            SuggestedConfidence = 0.8,
                            Rect = new RectD(0, 0, 10, 10)
                        }
                    ]
                    : []
            };
        }
    }

    [Fact]
    public void MainWindowViewModel_PreviousAndNextWrapInsideFilteredImages()
    {
        var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(settingsDirectory, "settings.json");
        Directory.CreateDirectory(settingsDirectory);

        try
        {
            var performanceService = new PerformanceConfigService(new HardwareInfoService(), settingsPath);
            using var session = new NativeEngineSession();
            using var viewModel = new MainWindowViewModel(performanceService, session, new PerformanceConfig
            {
                MaxRamMb = 1024,
                MaxVramMb = 1024,
                MaxDecodeThreads = 2,
                MaxIoThreads = 2,
                MaxInferenceJobs = 1,
                MaxPrefetchImages = 8
            });
            var replaceState = typeof(MainWindowViewModel).GetMethod("ReplaceState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(replaceState);

            replaceState!.Invoke(viewModel, [new ProjectState
            {
                Name = "sample",
                ProjectKind = ProjectKind.ObjectDetection,
                PerformanceConfig = new PerformanceConfig
                {
                    MaxRamMb = 1024,
                    MaxVramMb = 1024,
                    MaxDecodeThreads = 2,
                    MaxIoThreads = 2,
                    MaxInferenceJobs = 1,
                    MaxPrefetchImages = 8
                },
                Images =
                [
                    CreateImage("img-1", "a.png", hasSuggestion: true),
                    CreateImage("img-2", "b.png", hasSuggestion: false),
                    CreateImage("img-3", "c.png", hasSuggestion: true),
                    CreateImage("img-4", "d.png", hasSuggestion: true),
                    CreateImage("img-5", "e.png", hasSuggestion: true),
                    CreateImage("img-6", "f.png", hasSuggestion: true)
                ]
            }]);

            viewModel.SelectedImageFilter = ImageFilterMode.WithSuggestions;

            Assert.Equal("img-1", viewModel.SelectedImage?.Id);
            Assert.True(viewModel.PreviousImageCommand.CanExecute(null));
            Assert.True(viewModel.NextImageCommand.CanExecute(null));

            viewModel.SelectPreviousImage();
            Assert.Equal("img-6", viewModel.SelectedImage?.Id);

            viewModel.SelectNextImage();
            Assert.Equal("img-1", viewModel.SelectedImage?.Id);

            viewModel.SelectNextImage();
            viewModel.SelectNextImage();
            viewModel.SelectNextImage();
            viewModel.SelectNextImage();
            Assert.Equal("img-6", viewModel.SelectedImage?.Id);

            viewModel.SelectPreviousImage();
            Assert.Equal("img-5", viewModel.SelectedImage?.Id);

            viewModel.SelectedImage = viewModel.Images.First(image => image.Id == "img-2");
            Assert.True(viewModel.NextImageCommand.CanExecute(null));
            Assert.True(viewModel.PreviousImageCommand.CanExecute(null));

            viewModel.SelectNextImage();
            Assert.Equal("img-3", viewModel.SelectedImage?.Id);

            viewModel.SelectedImage = viewModel.Images.First(image => image.Id == "img-2");
            viewModel.SelectPreviousImage();
            Assert.Equal("img-1", viewModel.SelectedImage?.Id);
        }
        finally
        {
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        }

        static ImageRecord CreateImage(string id, string fileName, bool hasSuggestion)
        {
            return new ImageRecord
            {
                Id = id,
                Path = $@"C:\images\{fileName}",
                FileName = fileName,
                FileSizeBytes = 1234,
                PixelSize = new Size2D(800, 600),
                Annotations = hasSuggestion
                    ?
                    [
                        new AnnotationRecord
                        {
                            Id = $"ann-{id}",
                            Kind = AnnotationKind.Rect,
                            SuggestedLabel = "Enemy",
                            SuggestedConfidence = 0.8,
                            Rect = new RectD(0, 0, 10, 10)
                        }
                    ]
                    : []
            };
        }
    }

    [Fact]
    public void MainWindowViewModel_ConvertedNativeSuggestionsKeepAssignedLabelAndSuggestionState()
    {
        var convertMethod = typeof(MainWindowViewModel).GetMethod("ConvertNativeSuggestion", BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(convertMethod);

        var label = new LabelClass
        {
            Id = "label-1",
            Name = "target"
        };
        var suggestion = new NativeInferenceSuggestion(
            "annotation-1",
            MsInferenceSuggestionKind.Rect,
            0.9f,
            true,
            "raw",
            "raw",
            10,
            20,
            30,
            40,
            0,
            0);

        var converted = Assert.IsType<AnnotationRecord>(convertMethod!.Invoke(null, [suggestion, label]));

        Assert.Equal("label-1", converted.LabelId);
        Assert.Equal("target", converted.SuggestedLabel);
        Assert.NotNull(converted.Rect);
    }

    [Fact]
    public void MainWindowViewModel_UpdatingAnnotationsOnSameImage_PreservesViewportTransform()
    {
        var settingsDirectory = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        var settingsPath = Path.Combine(settingsDirectory, "settings.json");
        Directory.CreateDirectory(settingsDirectory);

        try
        {
            var performanceService = new PerformanceConfigService(new HardwareInfoService(), settingsPath);
            using var session = new NativeEngineSession();
            using var viewModel = new MainWindowViewModel(performanceService, session, new PerformanceConfig
            {
                MaxRamMb = 1024,
                MaxVramMb = 1024,
                MaxDecodeThreads = 2,
                MaxIoThreads = 2,
                MaxInferenceJobs = 1,
                MaxPrefetchImages = 8
            });

            var image = new ImageRecord
            {
                Id = "img-1",
                Path = @"C:\images\a.png",
                FileName = "a.png",
                FileSizeBytes = 1234,
                PixelSize = new Size2D(800, 600)
            };

            var state = new ProjectState
            {
                Name = "sample",
                ProjectKind = ProjectKind.ObjectDetection,
                PerformanceConfig = new PerformanceConfig
                {
                    MaxRamMb = 1024,
                    MaxVramMb = 1024,
                    MaxDecodeThreads = 2,
                    MaxIoThreads = 2,
                    MaxInferenceJobs = 1,
                    MaxPrefetchImages = 8
                },
                Images = [image]
            };

            var replaceState = typeof(MainWindowViewModel).GetMethod("ReplaceState", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(replaceState);
            replaceState!.Invoke(viewModel, [state]);

            viewModel.SelectedImage = viewModel.Images[0];
            viewModel.UpdateViewportTransform(2.5, 18, -12);

            viewModel.UpsertAnnotationFromViewport(new AnnotationRecord
            {
                Id = "ann-1",
                Kind = AnnotationKind.Rect,
                Rect = new RectD(10, 20, 30, 40)
            }, isUpdate: false);

            Assert.Equal(2.5, viewModel.ViewZoom);
            Assert.Equal(18, viewModel.PanOffsetX);
            Assert.Equal(-12, viewModel.PanOffsetY);
            Assert.Single(viewModel.Images[0].Annotations);
        }
        finally
        {
            if (Directory.Exists(settingsDirectory))
            {
                Directory.Delete(settingsDirectory, true);
            }
        }
    }
}

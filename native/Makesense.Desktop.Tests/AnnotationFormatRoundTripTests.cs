using System.IO.Compression;
using Makesense.Formats.Contracts;
using Makesense.Formats.Export;
using Makesense.Formats.Import;

namespace Makesense.Desktop.Tests;

public sealed class AnnotationFormatRoundTripTests
{
    [Fact]
    public void CocoImporter_MergesRectAndPolygonAnnotations()
    {
        var project = new ProjectState
        {
            Name = "sample",
            ProjectKind = ProjectKind.ObjectDetection,
            Images =
            [
                new ImageRecord
                {
                    Id = "img-1",
                    Path = @"C:\images\image-a.png",
                    FileName = "image-a.png",
                    FileSizeBytes = 12,
                    PixelSize = new Size2D(800, 600)
                }
            ]
        };

        var cocoPath = Path.GetTempFileName();
        File.WriteAllText(cocoPath,
            """
            {
              "images": [{ "id": 1, "file_name": "image-a.png", "width": 800, "height": 600 }],
              "categories": [{ "id": 3, "name": "person" }],
              "annotations": [
                { "id": 1, "image_id": 1, "category_id": 3, "bbox": [10, 20, 30, 40], "segmentation": [[10,20,40,20,25,60]] }
              ]
            }
            """);

        try
        {
            var service = new AnnotationImportService();
            var imported = service.Import(project, AnnotationFormat.Coco, [cocoPath]);

            Assert.Single(imported.Labels);
            Assert.Equal(2, imported.Images[0].Annotations.Count);
            Assert.Contains(imported.Images[0].Annotations, annotation => annotation.Kind == AnnotationKind.Rect);
            Assert.Contains(imported.Images[0].Annotations, annotation => annotation.Kind == AnnotationKind.Polygon);
        }
        finally
        {
            File.Delete(cocoPath);
        }
    }

    [Fact]
    public void YoloImporter_UsesPixelSizeToCreateRects()
    {
        var project = new ProjectState
        {
            Name = "sample",
            ProjectKind = ProjectKind.ObjectDetection,
            Images =
            [
                new ImageRecord
                {
                    Id = "img-1",
                    Path = @"C:\images\image-a.png",
                    FileName = "image-a.png",
                    FileSizeBytes = 12,
                    PixelSize = new Size2D(1000, 500)
                }
            ]
        };

        var tempDir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);
        var labelsPath = Path.Combine(tempDir, "labels.txt");
        var annotationPath = Path.Combine(tempDir, "image-a.txt");
        File.WriteAllText(labelsPath, "car");
        File.WriteAllText(annotationPath, "0 0.5 0.5 0.2 0.4");

        try
        {
            var service = new AnnotationImportService();
            var imported = service.Import(project, AnnotationFormat.Yolo, [labelsPath, annotationPath]);
            var rect = Assert.Single(imported.Images[0].Annotations);

            Assert.Equal(AnnotationKind.Rect, rect.Kind);
            Assert.NotNull(rect.Rect);
            Assert.Equal(400, rect.Rect!.Value.X, 3);
            Assert.Equal(150, rect.Rect!.Value.Y, 3);
            Assert.Equal(200, rect.Rect!.Value.Width, 3);
            Assert.Equal(200, rect.Rect!.Value.Height, 3);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Fact]
    public void YoloExporter_CreatesZipWithLabelsAndAnnotationFile()
    {
        var label = new LabelClass
        {
            Id = "label-1",
            Name = "car"
        };

        var project = new ProjectState
        {
            Name = "sample",
            ProjectKind = ProjectKind.ObjectDetection,
            Labels = [label],
            Images =
            [
                new ImageRecord
                {
                    Id = "img-1",
                    Path = @"C:\images\image-a.png",
                    FileName = "image-a.png",
                    FileSizeBytes = 12,
                    PixelSize = new Size2D(1000, 500),
                    Annotations =
                    [
                        new AnnotationRecord
                        {
                            Id = "ann-1",
                            Kind = AnnotationKind.Rect,
                            LabelId = label.Id,
                            Rect = new RectD(400, 150, 200, 200)
                        }
                    ]
                }
            ]
        };

        var zipPath = Path.GetTempFileName();
        File.Delete(zipPath);
        zipPath = Path.ChangeExtension(zipPath, ".zip");

        try
        {
            var exporter = new AnnotationExportService();
            exporter.Export(project, AnnotationFormat.Yolo, zipPath);

            using var archive = ZipFile.OpenRead(zipPath);
            Assert.NotNull(archive.GetEntry("labels.txt"));
            var imageEntry = archive.GetEntry("image-a.txt");
            Assert.NotNull(imageEntry);

            using var reader = new StreamReader(imageEntry!.Open());
            var content = reader.ReadToEnd();
            Assert.Contains("0 0.5 0.5 0.2 0.4", content, StringComparison.Ordinal);
        }
        finally
        {
            if (File.Exists(zipPath))
            {
                File.Delete(zipPath);
            }
        }
    }
}

using System.Globalization;
using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Makesense.Formats.Contracts;

namespace Makesense.Formats.Export;

public sealed class AnnotationExportService
{
    public void Export(ProjectState state, AnnotationFormat format, string outputPath)
    {
        switch (format)
        {
            case AnnotationFormat.Csv:
                File.WriteAllText(outputPath, CsvAnnotationExporter.Export(state));
                break;
            case AnnotationFormat.Yolo:
                ExportYolo(state, outputPath);
                break;
            case AnnotationFormat.YoloImageTxt:
                ExportYoloImageTxt(state, outputPath);
                break;
            case AnnotationFormat.Voc:
                ExportVoc(state, outputPath);
                break;
            case AnnotationFormat.Coco:
                File.WriteAllText(outputPath, ExportCoco(state));
                break;
            case AnnotationFormat.Vgg:
                File.WriteAllText(outputPath, ExportVgg(state));
                break;
            case AnnotationFormat.Json:
                File.WriteAllText(outputPath, ExportImageRecognitionJson(state));
                break;
            default:
                throw new NotSupportedException($"Export format {format} is not supported.");
        }
    }

    private static void ExportYolo(ProjectState state, string outputPath)
    {
        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        var labels = state.Labels.ToArray();

        var labelsEntry = archive.CreateEntry("labels.txt");
        using (var writer = new StreamWriter(labelsEntry.Open(), Encoding.UTF8))
        {
            foreach (var label in labels)
            {
                writer.WriteLine(label.Name);
            }
        }

        WriteYoloImageTextEntries(archive, state, labels);
    }

    private static void ExportYoloImageTxt(ProjectState state, string outputPath)
    {
        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        WriteYoloImageTextEntries(archive, state, state.Labels.ToArray());
    }

    private static void WriteYoloImageTextEntries(ZipArchive archive, ProjectState state, IReadOnlyList<LabelClass> labels)
    {
        var labelIndex = labels.Select((label, index) => new { label.Id, Index = index })
            .ToDictionary(item => item.Id, item => item.Index);

        foreach (var image in state.Images)
        {
            if (image.PixelSize is null)
            {
                continue;
            }

            var rects = image.Annotations.Where(annotation => annotation.Kind == AnnotationKind.Rect && annotation.LabelId is not null && annotation.Rect is not null).ToArray();
            if (rects.Length == 0)
            {
                continue;
            }

            var entry = archive.CreateEntry($"{Path.GetFileNameWithoutExtension(image.FileName)}.txt");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);

            foreach (var annotation in rects)
            {
                if (!labelIndex.TryGetValue(annotation.LabelId!, out var index))
                {
                    continue;
                }

                var rect = annotation.Rect!.Value;
                var cx = (rect.X + rect.Width / 2.0) / image.PixelSize.Value.Width;
                var cy = (rect.Y + rect.Height / 2.0) / image.PixelSize.Value.Height;
                var width = rect.Width / image.PixelSize.Value.Width;
                var height = rect.Height / image.PixelSize.Value.Height;
                writer.WriteLine($"{index} {Fmt(cx)} {Fmt(cy)} {Fmt(width)} {Fmt(height)}");
            }
        }
    }

    private static void ExportVoc(ProjectState state, string outputPath)
    {
        using var archive = ZipFile.Open(outputPath, ZipArchiveMode.Create);
        var labels = state.Labels.ToDictionary(label => label.Id, label => label.Name);

        foreach (var image in state.Images)
        {
            if (image.PixelSize is null)
            {
                continue;
            }

            var rects = image.Annotations.Where(annotation => annotation.Kind == AnnotationKind.Rect && annotation.LabelId is not null && annotation.Rect is not null).ToArray();
            if (rects.Length == 0)
            {
                continue;
            }

            var document = new XDocument(
                new XElement("annotation",
                    new XElement("filename", image.FileName),
                    new XElement("size",
                        new XElement("width", Math.Round(image.PixelSize.Value.Width)),
                        new XElement("height", Math.Round(image.PixelSize.Value.Height)),
                        new XElement("depth", 3)),
                    rects.Select(annotation =>
                    {
                        var rect = annotation.Rect!.Value;
                        return new XElement("object",
                            new XElement("name", labels.GetValueOrDefault(annotation.LabelId!, string.Empty)),
                            new XElement("bndbox",
                                new XElement("xmin", Math.Round(rect.X)),
                                new XElement("ymin", Math.Round(rect.Y)),
                                new XElement("xmax", Math.Round(rect.X + rect.Width)),
                                new XElement("ymax", Math.Round(rect.Y + rect.Height))));
                    })));

            var entry = archive.CreateEntry($"{Path.GetFileNameWithoutExtension(image.FileName)}.xml");
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            document.Save(writer);
        }
    }

    private static string ExportCoco(ProjectState state)
    {
        var polygonImages = state.Images.Where(image => image.Annotations.Any(annotation => annotation.Kind == AnnotationKind.Polygon && annotation.Polygon is not null)).ToArray();
        var labels = state.Labels.Select((label, index) => new { label, index }).ToArray();
        var labelsById = labels.ToDictionary(item => item.label.Id, item => item.index + 1);
        var images = polygonImages.Select((image, index) => new
        {
            id = index + 1,
            width = image.PixelSize?.Width ?? 0,
            height = image.PixelSize?.Height ?? 0,
            file_name = image.FileName
        }).ToArray();

        var categories = labels.Select(item => new
        {
            id = item.index + 1,
            name = item.label.Name
        }).ToArray();

        var annotations = new List<object>();
        var annotationId = 1;
        foreach (var image in polygonImages.Select((value, index) => new { value, index }))
        {
            foreach (var annotation in image.value.Annotations.Where(annotation => annotation.Kind == AnnotationKind.Polygon && annotation.Polygon is not null && annotation.LabelId is not null))
            {
                var points = annotation.Polygon!.ToArray();
                if (points.Length < 3)
                {
                    continue;
                }

                var xs = points.Select(point => point.X).ToArray();
                var ys = points.Select(point => point.Y).ToArray();
                var bbox = new[] { xs.Min(), ys.Min(), xs.Max() - xs.Min(), ys.Max() - ys.Min() };
                annotations.Add(new
                {
                    id = annotationId++,
                    iscrowd = 0,
                    image_id = image.index + 1,
                    category_id = labelsById[annotation.LabelId!],
                    segmentation = new[] { points.SelectMany(point => new[] { point.X, point.Y }).ToArray() },
                    bbox,
                    area = ComputePolygonArea(points)
                });
            }
        }

        return JsonSerializer.Serialize(new
        {
            info = new { description = state.Name },
            images,
            annotations,
            categories
        }, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ExportVgg(ProjectState state)
    {
        var labels = state.Labels.ToDictionary(label => label.Id, label => label.Name);
        var data = state.Images
            .Where(image => image.Annotations.Any(annotation => annotation.Kind == AnnotationKind.Polygon && annotation.Polygon is not null))
            .ToDictionary(
                image => image.FileName,
                image => new
                {
                    filename = image.FileName,
                    size = image.FileSizeBytes,
                    regions = image.Annotations
                        .Where(annotation => annotation.Kind == AnnotationKind.Polygon && annotation.Polygon is not null)
                        .Select(annotation => new
                        {
                            shape_attributes = new
                            {
                                name = "polygon",
                                all_points_x = annotation.Polygon!.Select(point => Math.Round(point.X)).ToArray(),
                                all_points_y = annotation.Polygon!.Select(point => Math.Round(point.Y)).ToArray()
                            },
                            region_attributes = new
                            {
                                label = annotation.LabelId is not null ? labels.GetValueOrDefault(annotation.LabelId, string.Empty) : string.Empty
                            }
                        }).ToArray(),
                    file_attributes = new { }
                });

        return JsonSerializer.Serialize(data, new JsonSerializerOptions { WriteIndented = true });
    }

    private static string ExportImageRecognitionJson(ProjectState state)
    {
        var labels = state.Labels.ToDictionary(label => label.Id, label => label.Name);
        var payload = state.Images.Select(image => new
        {
            image = image.FileName,
            labels = image.Annotations
                .Where(annotation => annotation.Kind == AnnotationKind.ImageRecognition && annotation.LabelId is not null)
                .Select(annotation => labels.GetValueOrDefault(annotation.LabelId!, string.Empty))
                .Where(name => !string.IsNullOrWhiteSpace(name))
                .Distinct()
                .ToArray()
        }).ToArray();

        return JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
    }

    private static double ComputePolygonArea(IReadOnlyList<Point2D> points)
    {
        double area = 0;
        for (var i = 0; i < points.Count; i++)
        {
            var j = (i + 1) % points.Count;
            area += points[i].X * points[j].Y - points[j].X * points[i].Y;
        }

        return Math.Abs(area / 2.0);
    }

    private static string Fmt(double value)
    {
        return Math.Clamp(value, 0.0, 1.0).ToString("0.######", CultureInfo.InvariantCulture);
    }
}

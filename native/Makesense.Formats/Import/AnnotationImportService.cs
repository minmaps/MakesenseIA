using System.Text.Json;
using System.Xml.Linq;
using Makesense.Formats.Contracts;
using Makesense.Formats.Project;

namespace Makesense.Formats.Import;

public sealed class AnnotationImportService
{
    public ProjectState Import(ProjectState state, AnnotationFormat format, IReadOnlyList<string> inputPaths)
    {
        if (state.Images.Count == 0)
        {
            throw new InvalidOperationException("Load images before importing annotations.");
        }

        return format switch
        {
            AnnotationFormat.Coco => ImportCoco(state, inputPaths.Single()),
            AnnotationFormat.Voc => ImportVoc(state, inputPaths),
            AnnotationFormat.Yolo => ImportYolo(state, inputPaths),
            _ => throw new NotSupportedException($"Import format {format} is not supported.")
        };
    }

    private static ProjectState ImportCoco(ProjectState state, string path)
    {
        using var stream = File.OpenRead(path);
        using var document = JsonDocument.Parse(stream);
        var root = document.RootElement;

        var labels = state.Labels.ToDictionary(label => label.Name, StringComparer.OrdinalIgnoreCase);
        var imagesByName = state.Images.ToDictionary(image => image.FileName, StringComparer.OrdinalIgnoreCase);
        var imageIdMap = new Dictionary<int, string>();

        foreach (var imageElement in root.GetProperty("images").EnumerateArray())
        {
            var fileName = imageElement.GetProperty("file_name").GetString() ?? string.Empty;
            if (imagesByName.TryGetValue(fileName, out var image))
            {
                imageIdMap[imageElement.GetProperty("id").GetInt32()] = image.Id;
            }
        }

        foreach (var categoryElement in root.GetProperty("categories").EnumerateArray())
        {
            var name = categoryElement.GetProperty("name").GetString() ?? "unnamed";
            if (!labels.ContainsKey(name))
            {
                labels[name] = new LabelClass
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = name
                };
            }
        }

        var labelsByCategoryId = root.GetProperty("categories")
            .EnumerateArray()
            .ToDictionary(
                category => category.GetProperty("id").GetInt32(),
                category => labels[category.GetProperty("name").GetString() ?? "unnamed"].Id);

        var imageAnnotations = state.Images.ToDictionary(
            image => image.Id,
            image => image.Annotations.Where(annotation => annotation.Kind is not AnnotationKind.Rect and not AnnotationKind.Polygon).ToList());

        foreach (var annotationElement in root.GetProperty("annotations").EnumerateArray())
        {
            var imageId = annotationElement.GetProperty("image_id").GetInt32();
            if (!imageIdMap.TryGetValue(imageId, out var targetImageId))
            {
                continue;
            }

            var labelId = labelsByCategoryId[annotationElement.GetProperty("category_id").GetInt32()];

            if (annotationElement.TryGetProperty("bbox", out var bboxElement) && bboxElement.GetArrayLength() == 4)
            {
                imageAnnotations[targetImageId].Add(new AnnotationRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = AnnotationKind.Rect,
                    LabelId = labelId,
                    Rect = new RectD(
                        bboxElement[0].GetDouble(),
                        bboxElement[1].GetDouble(),
                        bboxElement[2].GetDouble(),
                        bboxElement[3].GetDouble())
                });
            }

            if (annotationElement.TryGetProperty("segmentation", out var segmentationElement) &&
                segmentationElement.ValueKind == JsonValueKind.Array &&
                segmentationElement.GetArrayLength() > 0)
            {
                foreach (var polygonElement in segmentationElement.EnumerateArray())
                {
                    var values = polygonElement.EnumerateArray().Select(item => item.GetDouble()).ToArray();
                    if (values.Length < 6 || values.Length % 2 != 0)
                    {
                        continue;
                    }

                    var points = new List<Point2D>();
                    for (var index = 0; index < values.Length; index += 2)
                    {
                        points.Add(new Point2D(values[index], values[index + 1]));
                    }

                    imageAnnotations[targetImageId].Add(new AnnotationRecord
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Kind = AnnotationKind.Polygon,
                        LabelId = labelId,
                        Polygon = points
                    });
                }
            }
        }

        var mergedImages = state.Images
            .Select(image => image with { Annotations = imageAnnotations[image.Id].ToArray() })
            .ToArray();

        return state
            .ReplaceLabels(labels.Values.ToArray())
            .ReplaceImages(mergedImages);
    }

    private static ProjectState ImportVoc(ProjectState state, IReadOnlyList<string> inputPaths)
    {
        var labels = state.Labels.ToDictionary(label => label.Name, StringComparer.OrdinalIgnoreCase);
        var imagesByName = state.Images.ToDictionary(image => image.FileName, StringComparer.OrdinalIgnoreCase);
        var imageAnnotations = state.Images.ToDictionary(
            image => image.Id,
            image => image.Annotations.Where(annotation => annotation.Kind != AnnotationKind.Rect).ToList());

        foreach (var path in inputPaths)
        {
            var document = XDocument.Load(path);
            var annotation = document.Element("annotation");
            var fileName = annotation?.Element("filename")?.Value;
            if (string.IsNullOrWhiteSpace(fileName) || !imagesByName.TryGetValue(fileName, out var image))
            {
                continue;
            }

            foreach (var objectElement in annotation!.Elements("object"))
            {
                var labelName = objectElement.Element("name")?.Value ?? "unnamed";
                if (!labels.TryGetValue(labelName, out var label))
                {
                    label = new LabelClass
                    {
                        Id = Guid.NewGuid().ToString("N"),
                        Name = labelName
                    };
                    labels[labelName] = label;
                }

                var box = objectElement.Element("bndbox");
                if (box is null)
                {
                    continue;
                }

                var xmin = ParseDouble(box.Element("xmin")?.Value);
                var ymin = ParseDouble(box.Element("ymin")?.Value);
                var xmax = ParseDouble(box.Element("xmax")?.Value);
                var ymax = ParseDouble(box.Element("ymax")?.Value);

                imageAnnotations[image.Id].Add(new AnnotationRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = AnnotationKind.Rect,
                    LabelId = label.Id,
                    Rect = new RectD(xmin, ymin, Math.Max(0, xmax - xmin), Math.Max(0, ymax - ymin))
                });
            }
        }

        var mergedImages = state.Images
            .Select(image => image with { Annotations = imageAnnotations[image.Id].ToArray() })
            .ToArray();

        return state
            .ReplaceLabels(labels.Values.ToArray())
            .ReplaceImages(mergedImages);
    }

    private static ProjectState ImportYolo(ProjectState state, IReadOnlyList<string> inputPaths)
    {
        var labelFile = inputPaths.FirstOrDefault(path => string.Equals(Path.GetFileName(path), "labels.txt", StringComparison.OrdinalIgnoreCase));
        if (labelFile is null)
        {
            throw new InvalidOperationException("YOLO import requires labels.txt.");
        }

        var labelNames = File.ReadAllLines(labelFile)
            .Where(line => !string.IsNullOrWhiteSpace(line))
            .ToArray();

        var labels = state.Labels.ToDictionary(label => label.Name, StringComparer.OrdinalIgnoreCase);
        foreach (var labelName in labelNames)
        {
            if (!labels.ContainsKey(labelName))
            {
                labels[labelName] = new LabelClass
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Name = labelName
                };
            }
        }

        var labelsByIndex = labelNames
            .Select((name, index) => new { name, index })
            .ToDictionary(entry => entry.index, entry => labels[entry.name].Id);

        var imagesByStem = state.Images.ToDictionary(image => Path.GetFileNameWithoutExtension(image.FileName), StringComparer.OrdinalIgnoreCase);
        var imageAnnotations = state.Images.ToDictionary(
            image => image.Id,
            image => image.Annotations.Where(annotation => annotation.Kind != AnnotationKind.Rect).ToList());

        foreach (var path in inputPaths.Where(path => !string.Equals(path, labelFile, StringComparison.OrdinalIgnoreCase)))
        {
            var stem = Path.GetFileNameWithoutExtension(path);
            if (!imagesByStem.TryGetValue(stem, out var image) || image.PixelSize is null)
            {
                continue;
            }

            foreach (var line in File.ReadAllLines(path))
            {
                var values = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (values.Length != 5 || !int.TryParse(values[0], out var labelIndex))
                {
                    continue;
                }

                if (!labelsByIndex.TryGetValue(labelIndex, out var labelId))
                {
                    continue;
                }

                var cx = ParseDouble(values[1]) * image.PixelSize.Value.Width;
                var cy = ParseDouble(values[2]) * image.PixelSize.Value.Height;
                var width = ParseDouble(values[3]) * image.PixelSize.Value.Width;
                var height = ParseDouble(values[4]) * image.PixelSize.Value.Height;

                imageAnnotations[image.Id].Add(new AnnotationRecord
                {
                    Id = Guid.NewGuid().ToString("N"),
                    Kind = AnnotationKind.Rect,
                    LabelId = labelId,
                    Rect = new RectD(cx - width / 2.0, cy - height / 2.0, width, height)
                });
            }
        }

        var mergedImages = state.Images
            .Select(image => image with { Annotations = imageAnnotations[image.Id].ToArray() })
            .ToArray();

        return state
            .ReplaceLabels(labels.Values.ToArray())
            .ReplaceImages(mergedImages);
    }

    private static double ParseDouble(string? value)
    {
        return double.TryParse(value, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var result)
            ? result
            : 0.0;
    }
}

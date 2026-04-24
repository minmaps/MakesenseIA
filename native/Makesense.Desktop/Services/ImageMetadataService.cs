using System.IO;
using System.Windows.Media.Imaging;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.Services;

public sealed class ImageMetadataService
{
    public ImageRecord CreateRecord(string path, bool readPixelSize = true)
    {
        var fileInfo = new FileInfo(path);
        Size2D? pixelSize = readPixelSize ? ReadPixelSize(path) : null;

        return new ImageRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Path = path,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            PixelSize = pixelSize
        };
    }

    public Size2D ReadPixelSize(string path)
    {
        using var stream = new FileStream(
            path,
            FileMode.Open,
            FileAccess.Read,
            FileShare.Read,
            bufferSize: 4096,
            FileOptions.SequentialScan);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.None);
        var frame = decoder.Frames[0];

        return new Size2D(frame.PixelWidth, frame.PixelHeight);
    }
}

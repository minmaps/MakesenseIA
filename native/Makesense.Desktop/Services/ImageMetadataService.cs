using System.IO;
using System.Windows.Media.Imaging;
using Makesense.Formats.Contracts;

namespace Makesense.Desktop.Services;

public sealed class ImageMetadataService
{
    public ImageRecord CreateRecord(string path)
    {
        var fileInfo = new FileInfo(path);
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile,
            BitmapCacheOption.None);
        var frame = decoder.Frames[0];

        return new ImageRecord
        {
            Id = Guid.NewGuid().ToString("N"),
            Path = path,
            FileName = fileInfo.Name,
            FileSizeBytes = fileInfo.Length,
            PixelSize = new Size2D(frame.PixelWidth, frame.PixelHeight)
        };
    }
}

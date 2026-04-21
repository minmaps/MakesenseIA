using System.Collections.Concurrent;
using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;

namespace Makesense.Desktop.Converters;

public sealed class ImageThumbnailConverter : IValueConverter
{
    private readonly ConcurrentDictionary<string, BitmapSource> _cache = new(StringComparer.OrdinalIgnoreCase);

    public int DecodePixelWidth { get; set; } = 96;

    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string imagePath || string.IsNullOrWhiteSpace(imagePath) || !File.Exists(imagePath))
        {
            return null;
        }

        return _cache.GetOrAdd(imagePath, CreateThumbnail);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        throw new NotSupportedException();
    }

    private BitmapSource CreateThumbnail(string path)
    {
        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = new Uri(path, UriKind.Absolute);
        image.CacheOption = BitmapCacheOption.OnLoad;
        image.CreateOptions = BitmapCreateOptions.DelayCreation | BitmapCreateOptions.IgnoreColorProfile;
        image.DecodePixelWidth = DecodePixelWidth;
        image.EndInit();
        image.Freeze();
        return image;
    }
}

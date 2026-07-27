using System.Globalization;
using System.IO;
using System.Windows.Data;
using System.Windows.Media.Imaging;
using CameywareOrder.Services;

namespace CameywareOrder.Converters;

// Loads a stored document image into a thumbnail bitmap for square previews.
// The bytes are fully decoded on load (BitmapCacheOption.OnLoad) so the file is
// never left locked, and decoding is capped to keep memory small.
[ValueConversion(typeof(string), typeof(BitmapImage))]
public class DocumentThumbnailConverter : IValueConverter
{
    private const int DecodePixelSize = 128;

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string storedFileName || string.IsNullOrWhiteSpace(storedFileName))
            return null;

        var fullPath = DocumentStorageService.GetFullPath(storedFileName);
        if (!File.Exists(fullPath))
            return null;

        try
        {
            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.CreateOptions = BitmapCreateOptions.IgnoreColorProfile;
            bitmap.DecodePixelWidth = DecodePixelSize;
            bitmap.UriSource = new Uri(fullPath);
            bitmap.EndInit();
            bitmap.Freeze();
            return bitmap;
        }
        catch
        {
            return null;
        }
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}

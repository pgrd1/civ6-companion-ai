using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Civ6Companion.App.Capture;

public static class FrameQuality
{
    private const int SampleColumns = 32;
    private const int SampleRows = 18;
    private const double BlackLuminanceThreshold = 8;
    private const double UniformStandardDeviationThreshold = 2;

    public static bool IsUnusable(string imagePath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(imagePath);
        using var stream = new FileStream(imagePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var frame = BitmapFrame.Create(stream, BitmapCreateOptions.PreservePixelFormat, BitmapCacheOption.OnLoad);
        return IsUnusable(frame);
    }

    public static bool IsUnusable(BitmapSource frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        if (frame.PixelWidth <= 0 || frame.PixelHeight <= 0)
        {
            return true;
        }

        var normalized = frame.Format == PixelFormats.Bgra32
            ? frame
            : new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);

        var count = 0;
        var blackCount = 0;
        var mean = 0d;
        var sumOfSquareDifferences = 0d;
        var pixel = new byte[4];

        for (var row = 0; row < SampleRows; row++)
        {
            var y = Math.Min(normalized.PixelHeight - 1, ((row * 2 + 1) * normalized.PixelHeight) / (SampleRows * 2));
            for (var column = 0; column < SampleColumns; column++)
            {
                var x = Math.Min(normalized.PixelWidth - 1, ((column * 2 + 1) * normalized.PixelWidth) / (SampleColumns * 2));
                normalized.CopyPixels(new Int32Rect(x, y, 1, 1), pixel, 4, 0);

                var luminance = (0.0722 * pixel[0]) + (0.7152 * pixel[1]) + (0.2126 * pixel[2]);
                count++;
                if (luminance < BlackLuminanceThreshold)
                {
                    blackCount++;
                }

                var delta = luminance - mean;
                mean += delta / count;
                sumOfSquareDifferences += delta * (luminance - mean);
            }
        }

        var standardDeviation = Math.Sqrt(sumOfSquareDifferences / count);
        return blackCount >= Math.Ceiling(count * 0.98) || standardDeviation < UniformStandardDeviationThreshold;
    }
}

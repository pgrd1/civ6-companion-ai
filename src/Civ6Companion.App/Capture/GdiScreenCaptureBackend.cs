using System.ComponentModel;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Interop;
using System.Windows.Media.Imaging;

namespace Civ6Companion.App.Capture;

public sealed class GdiScreenCaptureBackend : ICaptureBackend
{
    private const int SourceCopy = 0x00CC0020;
    private const int CaptureLayeredWindows = 0x40000000;

    public Task CaptureAsync(CivWindow window, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateBounds(window.ClientBounds);

        if (!OperatingSystem.IsWindows())
        {
            throw new PlatformNotSupportedException("Desktop capture requires Windows.");
        }

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputPath))!);
        Capture(window.ClientBounds, outputPath, cancellationToken);
        return Task.CompletedTask;
    }

    private static void Capture(PixelRect bounds, string outputPath, CancellationToken cancellationToken)
    {
        nint screenDc = nint.Zero;
        nint memoryDc = nint.Zero;
        nint bitmap = nint.Zero;
        nint previousBitmap = nint.Zero;
        try
        {
            // CivWindow bounds are physical desktop pixels from ClientToScreen; GDI uses that same space.
            screenDc = GetDC(nint.Zero);
            if (screenDc == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            memoryDc = CreateCompatibleDC(screenDc);
            if (memoryDc == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            bitmap = CreateCompatibleBitmap(screenDc, bounds.Width, bounds.Height);
            if (bitmap == nint.Zero)
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            previousBitmap = SelectObject(memoryDc, bitmap);
            if (previousBitmap == nint.Zero || previousBitmap == new nint(-1))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            if (!BitBlt(memoryDc, 0, 0, bounds.Width, bounds.Height, screenDc, bounds.X, bounds.Y, SourceCopy | CaptureLayeredWindows))
            {
                throw new Win32Exception(Marshal.GetLastWin32Error());
            }

            cancellationToken.ThrowIfCancellationRequested();
            var source = Imaging.CreateBitmapSourceFromHBitmap(bitmap, nint.Zero, System.Windows.Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            source.Freeze();
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(source));
            using var output = new FileStream(outputPath, FileMode.Create, FileAccess.Write, FileShare.None);
            encoder.Save(output);
        }
        finally
        {
            if (previousBitmap != nint.Zero && memoryDc != nint.Zero)
            {
                _ = SelectObject(memoryDc, previousBitmap);
            }

            if (bitmap != nint.Zero)
            {
                _ = DeleteObject(bitmap);
            }

            if (memoryDc != nint.Zero)
            {
                _ = DeleteDC(memoryDc);
            }

            if (screenDc != nint.Zero)
            {
                _ = ReleaseDC(nint.Zero, screenDc);
            }
        }
    }

    private static void ValidateBounds(PixelRect bounds)
    {
        if (!bounds.HasArea || bounds.Width > 16_384 || bounds.Height > 16_384 || ((long)bounds.Width * bounds.Height) > 67_108_864)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds));
        }
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern nint GetDC(nint window);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern int ReleaseDC(nint window, nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint CreateCompatibleBitmap(nint deviceContext, int width, int height);

    [DllImport("gdi32.dll", SetLastError = true)]
    private static extern nint SelectObject(nint deviceContext, nint graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteObject(nint graphicsObject);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DeleteDC(nint deviceContext);

    [DllImport("gdi32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool BitBlt(
        nint destinationDeviceContext,
        int destinationX,
        int destinationY,
        int width,
        int height,
        nint sourceDeviceContext,
        int sourceX,
        int sourceY,
        int rasterOperation);
}

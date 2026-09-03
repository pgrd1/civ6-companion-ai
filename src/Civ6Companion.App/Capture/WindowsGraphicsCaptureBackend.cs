using System.ComponentModel;
using System.Runtime.InteropServices;

namespace Civ6Companion.App.Capture;

public sealed class WindowsGraphicsCaptureBackend : ICaptureBackend
{
    private const int MaximumDimension = 16_384;
    private const long MaximumPixels = 67_108_864;
    private readonly IWindowsGraphicsCaptureSessionFactory _sessionFactory;

    public WindowsGraphicsCaptureBackend() : this(new NativeWindowsGraphicsCaptureSessionFactory()) { }

    internal WindowsGraphicsCaptureBackend(IWindowsGraphicsCaptureSessionFactory sessionFactory) =>
        _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));

    public async Task CaptureAsync(CivWindow window, string outputPath, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputPath);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateBounds(window.ClientBounds);
        await using var session = _sessionFactory.Create(window);
        await session.CaptureFirstFrameAsync(outputPath, cancellationToken).ConfigureAwait(false);
    }

    private static void ValidateBounds(PixelRect bounds)
    {
        if (!bounds.HasArea || bounds.Width > MaximumDimension || bounds.Height > MaximumDimension ||
            ((long)bounds.Width * bounds.Height) > MaximumPixels)
            throw new ArgumentOutOfRangeException(nameof(bounds));
    }
}

internal interface IWindowsGraphicsCaptureSessionFactory
{
    IWindowsGraphicsCaptureSession Create(CivWindow window);
}

internal interface IWindowsGraphicsCaptureSession : IAsyncDisposable
{
    Task CaptureFirstFrameAsync(string outputPath, CancellationToken cancellationToken);
}

internal sealed class NativeWindowsGraphicsCaptureSessionFactory : IWindowsGraphicsCaptureSessionFactory
{
    public IWindowsGraphicsCaptureSession Create(CivWindow window) => new NativeWindowsGraphicsCaptureSession(window);
}

internal sealed class NativeWindowsGraphicsCaptureSession : IWindowsGraphicsCaptureSession
{
    private readonly CivWindow _window;
    private int _disposed;

    public NativeWindowsGraphicsCaptureSession(CivWindow window) => _window = window;

    public async Task CaptureFirstFrameAsync(string outputPath, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed != 0, this);
        using var cancelled = new EventWaitHandle(false, EventResetMode.ManualReset);
        using var registration = cancellationToken.Register(static state => ((EventWaitHandle)state!).Set(), cancelled);
        var nativeResult = await Task.Run(
            () => NativeMethods.CaptureCivWindowToPng(_window.Handle, _window.ClientBounds.X, _window.ClientBounds.Y,
                _window.ClientBounds.Width, _window.ClientBounds.Height, outputPath, cancelled.SafeWaitHandle.DangerousGetHandle()),
            CancellationToken.None).ConfigureAwait(false);
        cancellationToken.ThrowIfCancellationRequested();
        if (nativeResult < 0)
            throw new Win32Exception(nativeResult, "Windows Graphics Capture failed to produce a frame.");
    }

    public ValueTask DisposeAsync()
    {
        Interlocked.Exchange(ref _disposed, 1);
        return ValueTask.CompletedTask;
    }

    private static class NativeMethods
    {
        [DllImport("Civ6Companion.WgcNative.dll", CharSet = CharSet.Unicode, ExactSpelling = true, PreserveSig = true)]
        internal static extern int CaptureCivWindowToPng(nint window, int clientX, int clientY, int width, int height, string outputPath, nint cancelled);
    }
}

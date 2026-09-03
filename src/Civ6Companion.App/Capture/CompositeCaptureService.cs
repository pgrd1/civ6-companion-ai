using System.IO;

namespace Civ6Companion.App.Capture;

public sealed class CompositeCaptureService
{
    private const int MaximumDimension = 16_384;
    private const long MaximumPixels = 67_108_864;

    private readonly ICaptureBackend _primary;
    private readonly ICaptureBackend _fallback;
    private readonly string _temporaryRoot;
    private readonly bool _keepScreenshots;

    public CompositeCaptureService(
        ICaptureBackend primary,
        ICaptureBackend fallback,
        string temporaryRoot,
        bool keepScreenshots = false)
    {
        _primary = primary ?? throw new ArgumentNullException(nameof(primary));
        _fallback = fallback ?? throw new ArgumentNullException(nameof(fallback));
        _temporaryRoot = string.IsNullOrWhiteSpace(temporaryRoot)
            ? throw new ArgumentException("A temporary capture directory is required.", nameof(temporaryRoot))
            : Path.GetFullPath(temporaryRoot);
        _keepScreenshots = keepScreenshots;
    }

    public async Task<TemporaryCapture> CaptureAsync(CivWindow window, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(window);
        cancellationToken.ThrowIfCancellationRequested();
        ValidateBounds(window.ClientBounds);
        Directory.CreateDirectory(_temporaryRoot);

        var primaryPath = CreatePath();
        Exception? primaryFailure = null;
        try
        {
            await _primary.CaptureAsync(window, primaryPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (!IsUnusable(primaryPath))
            {
                return new TemporaryCapture(primaryPath, _keepScreenshots);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DeleteFile(primaryPath);
            throw;
        }
        catch (Exception exception)
        {
            primaryFailure = exception;
        }

        DeleteFile(primaryPath);
        return await CaptureFallbackAsync(window, cancellationToken, primaryFailure).ConfigureAwait(false);
    }

    private async Task<TemporaryCapture> CaptureFallbackAsync(
        CivWindow window,
        CancellationToken cancellationToken,
        Exception? primaryFailure)
    {
        var fallbackPath = CreatePath();
        try
        {
            await _fallback.CaptureAsync(window, fallbackPath, cancellationToken).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (IsUnusable(fallbackPath))
            {
                throw new InvalidOperationException("Both capture backends produced an unusable frame.");
            }

            return new TemporaryCapture(fallbackPath, _keepScreenshots);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            DeleteFile(fallbackPath);
            throw;
        }
        catch (Exception fallbackFailure)
        {
            DeleteFile(fallbackPath);
            if (primaryFailure is not null)
            {
                throw new AggregateException("Both capture backends failed.", primaryFailure, fallbackFailure);
            }

            throw;
        }
    }

    private string CreatePath() => Path.Combine(_temporaryRoot, $"civ6-{Guid.NewGuid():N}.png");

    private static bool IsUnusable(string path) => !File.Exists(path) || FrameQuality.IsUnusable(path);

    private static void ValidateBounds(PixelRect bounds)
    {
        if (!bounds.HasArea || bounds.Width > MaximumDimension || bounds.Height > MaximumDimension ||
            ((long)bounds.Width * bounds.Height) > MaximumPixels)
        {
            throw new ArgumentOutOfRangeException(nameof(bounds), "Capture bounds exceed the safe resource limit.");
        }
    }

    private static void DeleteFile(string path)
    {
        try
        {
            File.Delete(path);
        }
        catch (IOException)
        {
            // A failed cleanup cannot make the capture succeed, and disposal may retry it later.
        }
        catch (UnauthorizedAccessException)
        {
            // The temporary owner never escalates privileges to remove an inaccessible file.
        }
    }
}

public sealed class TemporaryCapture : IAsyncDisposable
{
    private readonly bool _keepScreenshots;
    private int _disposed;

    internal TemporaryCapture(string path, bool keepScreenshots)
    {
        Path = path;
        _keepScreenshots = keepScreenshots;
    }

    public string Path { get; }

    public ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 0 && !_keepScreenshots)
        {
            try
            {
                File.Delete(Path);
            }
            catch (IOException)
            {
                // Deletion is best effort; an open PNG cannot be safely force-removed.
            }
            catch (UnauthorizedAccessException)
            {
                // Keep cleanup scoped to the current user's permissions.
            }
        }

        return ValueTask.CompletedTask;
    }
}

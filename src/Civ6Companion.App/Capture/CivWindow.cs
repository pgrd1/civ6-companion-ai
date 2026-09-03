using System.Diagnostics.CodeAnalysis;

namespace Civ6Companion.App.Capture;

public sealed record PixelRect(int X, int Y, int Width, int Height)
{
    public bool HasArea => Width > 0 && Height > 0;

    public static bool TryCreateFromClientEdges(
        int left,
        int top,
        int right,
        int bottom,
        [NotNullWhen(true)] out PixelRect? bounds)
    {
        var width = (long)right - left;
        var height = (long)bottom - top;
        if (width is < 0 or > int.MaxValue || height is < 0 or > int.MaxValue)
        {
            bounds = null;
            return false;
        }

        bounds = new PixelRect(left, top, (int)width, (int)height);
        return true;
    }
}

public sealed record CivWindow(
    nint Handle,
    int ProcessId,
    PixelRect ClientBounds,
    string Title,
    bool IsForeground);

public sealed record WindowCandidate(
    nint Handle,
    int ProcessId,
    string? ProcessName,
    PixelRect ClientBounds,
    string? Title,
    bool IsVisible,
    bool IsMinimized,
    bool IsCloaked,
    bool IsForeground);

public interface IWindowApi
{
    IReadOnlyList<WindowCandidate> GetTopLevelWindows();
}

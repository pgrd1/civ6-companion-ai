using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace Civ6Companion.App.Capture;

public sealed class CivWindowLocator : ICivWindowLocator
{
    private static readonly HashSet<string> SupportedProcessNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CivilizationVI",
        "CivilizationVI_DX11",
        "CivilizationVI_DX12"
    };

    private readonly IWindowApi _windowApi;

    public CivWindowLocator(IWindowApi windowApi)
    {
        _windowApi = windowApi ?? throw new ArgumentNullException(nameof(windowApi));
    }

    public Task<WindowLookupResult> FindAsync(CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var civCandidates = _windowApi.GetTopLevelWindows()
            .Where(IsCivCandidate)
            .ToArray();

        if (civCandidates.Length == 0)
        {
            return Task.FromResult(WindowLookupResult.Failed(WindowLookupFailure.NotRunning));
        }

        var visibleCandidates = civCandidates
            .Where(candidate => candidate.IsVisible && !candidate.IsCloaked)
            .ToArray();
        if (visibleCandidates.Length == 0)
        {
            return Task.FromResult(WindowLookupResult.Failed(WindowLookupFailure.NotRunning));
        }

        var restoredCandidates = visibleCandidates.Where(candidate => !candidate.IsMinimized).ToArray();
        if (restoredCandidates.Length == 0)
        {
            return Task.FromResult(WindowLookupResult.Failed(WindowLookupFailure.Minimized));
        }

        var validCandidates = restoredCandidates.Where(candidate => candidate.ClientBounds.HasArea).ToArray();
        if (validCandidates.Length == 0)
        {
            return Task.FromResult(WindowLookupResult.Failed(WindowLookupFailure.InvalidBounds));
        }

        var selectedCandidate = validCandidates
            .Where(candidate => candidate.IsForeground)
            .OrderBy(candidate => candidate.ProcessId)
            .ThenBy(candidate => candidate.Handle.ToInt64())
            .FirstOrDefault();

        if (selectedCandidate is null)
        {
            return Task.FromResult(WindowLookupResult.Failed(WindowLookupFailure.NotForeground));
        }

        var window = new CivWindow(
            selectedCandidate.Handle,
            selectedCandidate.ProcessId,
            selectedCandidate.ClientBounds,
            selectedCandidate.Title ?? string.Empty,
            IsForeground: true);

        return Task.FromResult(WindowLookupResult.Found(window));
    }

    private static bool IsCivCandidate(WindowCandidate candidate) =>
        IsSupportedProcessName(candidate.ProcessName) ||
        (string.IsNullOrWhiteSpace(candidate.ProcessName) && IsCivTitle(candidate.Title));

    private static bool IsSupportedProcessName(string? processName) =>
        processName is not null && SupportedProcessNames.Contains(processName);

    private static bool IsCivTitle(string? title) =>
        !string.IsNullOrWhiteSpace(title) &&
        title.Contains("Civilization VI", StringComparison.OrdinalIgnoreCase) &&
        !title.Contains("launcher", StringComparison.OrdinalIgnoreCase);
}

public sealed class Win32WindowApi : IWindowApi
{
    private const int DwmWindowAttributeCloaked = 14;

    public IReadOnlyList<WindowCandidate> GetTopLevelWindows()
    {
        if (!OperatingSystem.IsWindows())
        {
            return Array.Empty<WindowCandidate>();
        }

        var foregroundWindow = GetForegroundWindow();
        var candidates = new List<WindowCandidate>();
        _ = EnumWindows((handle, _) =>
        {
            var candidate = TryReadWindow(handle, foregroundWindow);
            if (candidate is not null)
            {
                candidates.Add(candidate);
            }

            return true;
        }, nint.Zero);

        return candidates;
    }

    private static WindowCandidate? TryReadWindow(nint handle, nint foregroundWindow)
    {
        if (GetWindowThreadProcessId(handle, out var processId) == 0 || processId == 0)
        {
            return null;
        }

        if (!GetClientRect(handle, out var clientRect))
        {
            return null;
        }

        var topLeft = new NativePoint(clientRect.Left, clientRect.Top);
        var bottomRight = new NativePoint(clientRect.Right, clientRect.Bottom);
        if (!ClientToScreen(handle, ref topLeft) || !ClientToScreen(handle, ref bottomRight))
        {
            return null;
        }

        if (!PixelRect.TryCreateFromClientEdges(
                topLeft.X,
                topLeft.Y,
                bottomRight.X,
                bottomRight.Y,
                out var clientBounds))
        {
            return null;
        }

        return new WindowCandidate(
            handle,
            unchecked((int)processId),
            TryGetProcessName(unchecked((int)processId)),
            clientBounds,
            GetWindowTitle(handle),
            IsWindowVisible(handle),
            IsIconic(handle),
            IsCloaked(handle),
            handle == foregroundWindow);
    }

    private static string? TryGetProcessName(int processId)
    {
        try
        {
            using var process = Process.GetProcessById(processId);
            return process.HasExited ? null : process.ProcessName;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (Win32Exception)
        {
            return null;
        }
    }

    private static string GetWindowTitle(nint handle)
    {
        var length = GetWindowTextLength(handle);
        if (length <= 0)
        {
            return string.Empty;
        }

        var title = new StringBuilder(length + 1);
        _ = GetWindowText(handle, title, title.Capacity);
        return title.ToString();
    }

    private static bool IsCloaked(nint handle)
    {
        try
        {
            return DwmGetWindowAttribute(
                handle,
                DwmWindowAttributeCloaked,
                out var cloaked,
                Marshal.SizeOf<int>()) == 0 && cloaked != 0;
        }
        catch (DllNotFoundException)
        {
            return false;
        }
        catch (EntryPointNotFoundException)
        {
            return false;
        }
    }

    private delegate bool EnumWindowsProc(nint handle, nint parameter);

    [DllImport("user32.dll")]
    private static extern bool EnumWindows(EnumWindowsProc callback, nint parameter);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(nint handle, out uint processId);

    [DllImport("user32.dll")]
    private static extern bool GetClientRect(nint handle, out NativeRect clientRect);

    [DllImport("user32.dll")]
    private static extern bool ClientToScreen(nint handle, ref NativePoint point);

    [DllImport("user32.dll")]
    private static extern bool IsWindowVisible(nint handle);

    [DllImport("user32.dll")]
    private static extern bool IsIconic(nint handle);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowText(nint handle, StringBuilder title, int maximumCount);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern int GetWindowTextLength(nint handle);

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmGetWindowAttribute(
        nint handle,
        int attribute,
        out int attributeValue,
        int attributeSize);

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint(int x, int y)
    {
        public int X = x;
        public int Y = y;
    }
}

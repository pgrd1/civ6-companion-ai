using Civ6Companion.App.Capture;

namespace Civ6Companion.Tests.TestSupport;

public sealed class StubWindowApi : IWindowApi
{
    private readonly IReadOnlyList<WindowCandidate> _windows;

    public StubWindowApi(IReadOnlyList<WindowCandidate> windows)
    {
        ArgumentNullException.ThrowIfNull(windows);
        _windows = windows.ToArray();
    }

    public IReadOnlyList<WindowCandidate> GetTopLevelWindows() => _windows;
}

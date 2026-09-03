using Civ6Companion.App.Capture;

namespace Civ6Companion.App.Advisor;

internal sealed class CaptureQueue : IAsyncDisposable
{
    private readonly int _capacity;
    private readonly List<TemporaryCapture> _captures = [];

    public CaptureQueue(int capacity)
    {
        if (capacity < 1) throw new ArgumentOutOfRangeException(nameof(capacity));
        _capacity = capacity;
    }

    public int Count => _captures.Count;
    public IReadOnlyList<string> Paths => _captures.Select(capture => capture.Path).ToArray();

    public async ValueTask AddAsync(TemporaryCapture capture)
    {
        ArgumentNullException.ThrowIfNull(capture);
        if (_captures.Count == _capacity)
        {
            var oldest = _captures[0];
            _captures.RemoveAt(0);
            await oldest.DisposeAsync().ConfigureAwait(false);
        }
        _captures.Add(capture);
    }

    public async ValueTask ClearAsync()
    {
        var captures = _captures.ToArray();
        _captures.Clear();
        foreach (var capture in captures) await capture.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => ClearAsync();
}

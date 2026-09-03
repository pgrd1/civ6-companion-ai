using Civ6Companion.App.Capture;

namespace Civ6Companion.Tests.TestSupport;

public sealed class StubCaptureBackend : ICaptureBackend
{
    private readonly string _fixture;
    private readonly bool _throwAfterWriting;
    private readonly Action? _afterWriting;

    public StubCaptureBackend(string fixture, bool throwAfterWriting = false, Action? afterWriting = null)
    {
        _fixture = fixture;
        _throwAfterWriting = throwAfterWriting;
        _afterWriting = afterWriting;
    }

    public int CallCount { get; private set; }

    public Task CaptureAsync(CivWindow window, string outputPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        CallCount++;
        System.IO.File.Copy(FixtureFiles.Path(_fixture), outputPath, overwrite: true);
        _afterWriting?.Invoke();
        cancellationToken.ThrowIfCancellationRequested();
        if (_throwAfterWriting)
        {
            throw new InvalidOperationException("Configured capture failure.");
        }

        return Task.CompletedTask;
    }
}

using Civ6Companion.App.Capture;
using Civ6Companion.Tests.TestSupport;
using FluentAssertions;

namespace Civ6Companion.Tests.Capture;

public sealed class WindowsGraphicsCaptureBackendTests : IDisposable
{
    private readonly string _tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"WgcBackendTests-{Guid.NewGuid():N}");
    private readonly CivWindow _window = new((nint)77, 4321, new PixelRect(10, 20, 640, 360), "Civilization VI", true);

    [Fact]
    public async Task CaptureAsync_WhenNativeCaptureSucceeds_DisposesSessionAfterWritingPng()
    {
        var session = new StubWgcSession((path, _) => CopyFixture(path));
        var backend = new WindowsGraphicsCaptureBackend(new StubWgcFactory(session));
        var path = System.IO.Path.Combine(_tempRoot, "capture.png");

        await backend.CaptureAsync(_window, path, CancellationToken.None);

        System.IO.File.Exists(path).Should().BeTrue();
        session.SessionDisposed.Should().BeTrue();
        session.FramePoolDisposed.Should().BeTrue();
        session.FrameDisposed.Should().BeTrue();
        session.DeviceDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task CaptureAsync_WhenNativeCaptureFails_DisposesSession()
    {
        var session = new StubWgcSession((_, _) => throw new InvalidOperationException("native capture failed"));
        var backend = new WindowsGraphicsCaptureBackend(new StubWgcFactory(session));

        await FluentActions.Invoking(() => backend.CaptureAsync(_window, System.IO.Path.Combine(_tempRoot, "capture.png"), CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        session.SessionDisposed.Should().BeTrue();
        session.FramePoolDisposed.Should().BeTrue();
        session.FrameDisposed.Should().BeTrue();
        session.DeviceDisposed.Should().BeTrue();
    }

    [Fact]
    public async Task CaptureAsync_WhenNativeCaptureCancels_DisposesSession()
    {
        using var cancellation = new CancellationTokenSource();
        var session = new StubWgcSession((_, token) =>
        {
            cancellation.Cancel();
            token.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        });
        var backend = new WindowsGraphicsCaptureBackend(new StubWgcFactory(session));

        await FluentActions.Invoking(() => backend.CaptureAsync(_window, System.IO.Path.Combine(_tempRoot, "capture.png"), cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        session.SessionDisposed.Should().BeTrue();
        session.FramePoolDisposed.Should().BeTrue();
        session.FrameDisposed.Should().BeTrue();
        session.DeviceDisposed.Should().BeTrue();
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_tempRoot))
            System.IO.Directory.Delete(_tempRoot, recursive: true);
    }

    private Task CopyFixture(string path)
    {
        System.IO.Directory.CreateDirectory(_tempRoot);
        System.IO.File.Copy(FixtureFiles.Path("civ-map-sample.png"), path, overwrite: true);
        return Task.CompletedTask;
    }

    private sealed class StubWgcFactory(StubWgcSession session) : IWindowsGraphicsCaptureSessionFactory
    {
        public IWindowsGraphicsCaptureSession Create(CivWindow window) => session;
    }

    private sealed class StubWgcSession(Func<string, CancellationToken, Task> capture) : IWindowsGraphicsCaptureSession
    {
        public bool SessionDisposed { get; private set; }
        public bool FramePoolDisposed { get; private set; }
        public bool FrameDisposed { get; private set; }
        public bool DeviceDisposed { get; private set; }

        public Task CaptureFirstFrameAsync(string outputPath, CancellationToken cancellationToken) => capture(outputPath, cancellationToken);

        public ValueTask DisposeAsync()
        {
            SessionDisposed = true;
            FramePoolDisposed = true;
            FrameDisposed = true;
            DeviceDisposed = true;
            return ValueTask.CompletedTask;
        }
    }
}

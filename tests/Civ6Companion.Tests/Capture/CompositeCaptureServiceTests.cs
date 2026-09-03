using Civ6Companion.App.Capture;
using Civ6Companion.Tests.TestSupport;
using FluentAssertions;

namespace Civ6Companion.Tests.Capture;

public sealed class CompositeCaptureServiceTests : IDisposable
{
    private readonly string _tempRoot = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"Civ6CaptureTests-{Guid.NewGuid():N}");
    private readonly CivWindow _window = new((nint)42, 1234, new PixelRect(10, 20, 640, 360), "Civilization VI", true);

    [Fact]
    public async Task CaptureAsync_WhenPrimaryIsBlack_UsesFallbackAndDeletesOnDispose()
    {
        var primary = new StubCaptureBackend("black-frame.png");
        var fallback = new StubCaptureBackend("civ-map-sample.png");
        var service = new CompositeCaptureService(primary, fallback, _tempRoot);

        string path;
        await using (var capture = await service.CaptureAsync(_window, CancellationToken.None))
        {
            path = capture.Path;
            System.IO.File.Exists(path).Should().BeTrue();
            primary.CallCount.Should().Be(1);
            fallback.CallCount.Should().Be(1);
        }

        System.IO.File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public async Task CaptureAsync_WhenPrimaryIsUsable_DoesNotInvokeFallback()
    {
        var primary = new StubCaptureBackend("civ-map-sample.png");
        var fallback = new StubCaptureBackend("black-frame.png");
        var service = new CompositeCaptureService(primary, fallback, _tempRoot);

        await using var capture = await service.CaptureAsync(_window, CancellationToken.None);

        fallback.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CaptureAsync_WhenPrimaryFailsAfterWriting_UsesFallbackAndDeletesBothArtifacts()
    {
        var primary = new StubCaptureBackend("civ-map-sample.png", throwAfterWriting: true);
        var fallback = new StubCaptureBackend("civ-map-sample.png");
        var service = new CompositeCaptureService(primary, fallback, _tempRoot);

        string path;
        await using (var capture = await service.CaptureAsync(_window, CancellationToken.None))
        {
            path = capture.Path;
            fallback.CallCount.Should().Be(1);
            System.IO.File.Exists(path).Should().BeTrue();
        }

        System.IO.Directory.Exists(_tempRoot).Should().BeTrue();
        System.IO.Directory.EnumerateFiles(_tempRoot).Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureAsync_WhenCancelled_DoesNotInvokeBackends()
    {
        var primary = new StubCaptureBackend("civ-map-sample.png");
        var fallback = new StubCaptureBackend("civ-map-sample.png");
        var service = new CompositeCaptureService(primary, fallback, _tempRoot);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        await FluentActions.Invoking(() => service.CaptureAsync(_window, cancelled.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        primary.CallCount.Should().Be(0);
        fallback.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CaptureAsync_WhenBoundsExceedResourceLimit_RejectsBeforeBackendUse()
    {
        var primary = new StubCaptureBackend("civ-map-sample.png");
        var fallback = new StubCaptureBackend("civ-map-sample.png");
        var service = new CompositeCaptureService(primary, fallback, _tempRoot);
        var oversized = _window with { ClientBounds = new PixelRect(0, 0, 20_000, 20_000) };

        await FluentActions.Invoking(() => service.CaptureAsync(oversized, CancellationToken.None))
            .Should().ThrowAsync<ArgumentOutOfRangeException>();

        primary.CallCount.Should().Be(0);
    }

    [Fact]
    public async Task CaptureAsync_WhenCancellationOccursAfterPrimaryWrite_DeletesArtifactAndSkipsFallback()
    {
        using var cancellation = new CancellationTokenSource();
        var primary = new StubCaptureBackend("civ-map-sample.png", afterWriting: cancellation.Cancel);
        var fallback = new StubCaptureBackend("civ-map-sample.png");
        var service = new CompositeCaptureService(primary, fallback, _tempRoot);

        await FluentActions.Invoking(() => service.CaptureAsync(_window, cancellation.Token))
            .Should().ThrowAsync<OperationCanceledException>();

        fallback.CallCount.Should().Be(0);
        System.IO.Directory.EnumerateFiles(_tempRoot).Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureAsync_WhenFallbackIsUnusable_DeletesAllArtifacts()
    {
        var service = new CompositeCaptureService(
            new StubCaptureBackend("black-frame.png"),
            new StubCaptureBackend("black-frame.png"),
            _tempRoot);

        await FluentActions.Invoking(() => service.CaptureAsync(_window, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        System.IO.Directory.EnumerateFiles(_tempRoot).Should().BeEmpty();
    }

    [Fact]
    public async Task CaptureAsync_WhenFallbackFailsAfterWriting_DeletesAllArtifacts()
    {
        var service = new CompositeCaptureService(
            new StubCaptureBackend("black-frame.png"),
            new StubCaptureBackend("civ-map-sample.png", throwAfterWriting: true),
            _tempRoot);

        await FluentActions.Invoking(() => service.CaptureAsync(_window, CancellationToken.None))
            .Should().ThrowAsync<InvalidOperationException>();

        System.IO.Directory.EnumerateFiles(_tempRoot).Should().BeEmpty();
    }

    [Fact]
    public async Task TemporaryCapture_WhenKeepingScreenshots_RetainsCaptureAfterIdempotentDispose()
    {
        var service = new CompositeCaptureService(
            new StubCaptureBackend("civ-map-sample.png"),
            new StubCaptureBackend("black-frame.png"),
            _tempRoot,
            keepScreenshots: true);
        var capture = await service.CaptureAsync(_window, CancellationToken.None);

        await capture.DisposeAsync();
        await capture.DisposeAsync();

        System.IO.File.Exists(capture.Path).Should().BeTrue();
        System.IO.File.Delete(capture.Path);
    }

    [Fact]
    public async Task TemporaryCapture_WhenDisposedTwice_DeletesDefaultCaptureOnce()
    {
        var service = new CompositeCaptureService(
            new StubCaptureBackend("civ-map-sample.png"),
            new StubCaptureBackend("black-frame.png"),
            _tempRoot);
        var capture = await service.CaptureAsync(_window, CancellationToken.None);

        await capture.DisposeAsync();
        await capture.DisposeAsync();

        System.IO.File.Exists(capture.Path).Should().BeFalse();
    }

    public void Dispose()
    {
        if (System.IO.Directory.Exists(_tempRoot))
        {
            System.IO.Directory.Delete(_tempRoot, recursive: true);
        }
    }
}

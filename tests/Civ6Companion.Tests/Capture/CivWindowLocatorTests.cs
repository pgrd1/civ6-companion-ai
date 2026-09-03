using Civ6Companion.App.Capture;
using Civ6Companion.Tests.TestSupport;
using FluentAssertions;

namespace Civ6Companion.Tests.Capture;

public sealed class CivWindowLocatorTests
{
    [Fact]
    public async Task FindAsync_PrefersForegroundCivWindow()
    {
        var windows = new[]
        {
            Candidate(10, "Sid Meier's Civilization VI (DX12)", "CivilizationVI_DX12", isForeground: false),
            Candidate(20, "Sid Meier's Civilization VI (DX11)", "CivilizationVI", isForeground: true)
        };
        var locator = new CivWindowLocator(new StubWindowApi(windows));

        var result = await locator.FindAsync(CancellationToken.None);

        result.Window!.Handle.Should().Be((nint)20);
    }

    [Fact]
    public async Task FindAsync_WhenOnlyAVisibleNonForegroundCivWindowExists_ReturnsNotForeground()
    {
        var locator = new CivWindowLocator(new StubWindowApi(
        [
            Candidate(10, "Sid Meier's Civilization VI", "CivilizationVI_DX12", isForeground: false)
        ]));

        var result = await locator.FindAsync(CancellationToken.None);

        result.Should().Be(new WindowLookupResult(null, WindowLookupFailure.NotForeground));
    }

    [Fact]
    public async Task FindAsync_WhenEveryCivWindowIsMinimized_ReturnsMinimized()
    {
        var locator = new CivWindowLocator(new StubWindowApi(
        [
            Candidate(10, "Sid Meier's Civilization VI", "CivilizationVI", isMinimized: true)
        ]));

        var result = await locator.FindAsync(CancellationToken.None);

        result.Should().Be(new WindowLookupResult(null, WindowLookupFailure.Minimized));
    }

    [Fact]
    public async Task FindAsync_WhenEveryVisibleCivWindowHasEmptyBounds_ReturnsInvalidBounds()
    {
        var locator = new CivWindowLocator(new StubWindowApi(
        [
            Candidate(10, "Sid Meier's Civilization VI", "CivilizationVI", width: 0)
        ]));

        var result = await locator.FindAsync(CancellationToken.None);

        result.Should().Be(new WindowLookupResult(null, WindowLookupFailure.InvalidBounds));
    }

    [Fact]
    public async Task FindAsync_IgnoresInvisibleCivWindows()
    {
        var locator = new CivWindowLocator(new StubWindowApi(
        [
            Candidate(10, "Sid Meier's Civilization VI", "CivilizationVI", isVisible: false, isForeground: true)
        ]));

        var result = await locator.FindAsync(CancellationToken.None);

        result.Should().Be(new WindowLookupResult(null, WindowLookupFailure.NotRunning));
    }

    [Fact]
    public async Task FindAsync_IgnoresCloakedCivWindows()
    {
        var locator = new CivWindowLocator(new StubWindowApi(
        [
            Candidate(10, "Sid Meier's Civilization VI", "CivilizationVI", isCloaked: true, isForeground: true)
        ]));

        var result = await locator.FindAsync(CancellationToken.None);

        result.Should().Be(new WindowLookupResult(null, WindowLookupFailure.NotRunning));
    }

    [Fact]
    public async Task FindAsync_IgnoresLaunchersAndUnrelatedProcesses()
    {
        var locator = new CivWindowLocator(new StubWindowApi(
        [
            Candidate(10, "Sid Meier's Civilization VI - 2K Launcher", "Launcher", isForeground: true),
            Candidate(20, "Unrelated application", "NotCivilizationVI", isForeground: true)
        ]));

        var result = await locator.FindAsync(CancellationToken.None);

        result.Should().Be(new WindowLookupResult(null, WindowLookupFailure.NotRunning));
    }

    [Theory]
    [InlineData("Sid Meier's Civilization VI")]
    [InlineData("Sid Meier’s Civilization VI (DX12)")]
    [InlineData("SID MEIER'S CIVILIZATION VI - DirectX 11")]
    public async Task FindAsync_UsesTitleVariantsAsFallbackWhenProcessMetadataIsUnavailable(string title)
    {
        var locator = new CivWindowLocator(new StubWindowApi(
        [
            Candidate(30, title, processName: null, isForeground: true)
        ]));

        var result = await locator.FindAsync(CancellationToken.None);

        result.Window!.Title.Should().Be(title);
    }

    [Fact]
    public async Task FindAsync_PicksTheLowestProcessAndWindowHandleWhenForegroundMetadataTies()
    {
        var locator = new CivWindowLocator(new StubWindowApi(
        [
            Candidate(30, "Civ VI", "CivilizationVI_DX12", isForeground: true, processId: 5),
            Candidate(20, "Civ VI", "CivilizationVI", isForeground: true, processId: 3),
            Candidate(10, "Civ VI", "CivilizationVI_DX11", isForeground: true, processId: 3)
        ]));

        var result = await locator.FindAsync(CancellationToken.None);

        result.Window!.Handle.Should().Be((nint)10);
    }

    [Fact]
    public async Task FindAsync_HonorsCancellationBeforeEnumeratingWindows()
    {
        using var cancellationSource = new CancellationTokenSource();
        cancellationSource.Cancel();
        var locator = new CivWindowLocator(new StubWindowApi([]));

        var action = () => locator.FindAsync(cancellationSource.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData(int.MinValue, 0, int.MaxValue, 1080)]
    [InlineData(0, int.MaxValue, 1920, int.MinValue)]
    public void TryCreateFromClientEdges_WhenDimensionsExceedTheIntegerRange_ReturnsFalse(
        int left,
        int top,
        int right,
        int bottom)
    {
        var created = PixelRect.TryCreateFromClientEdges(left, top, right, bottom, out var bounds);

        created.Should().BeFalse();
        bounds.Should().BeNull();
    }

    private static WindowCandidate Candidate(
        int handle,
        string title,
        string? processName,
        bool isVisible = true,
        bool isMinimized = false,
        bool isCloaked = false,
        bool isForeground = false,
        int width = 1920,
        int height = 1080,
        int processId = 1) =>
        new(
            (nint)handle,
            processId,
            processName,
            new PixelRect(100, 200, width, height),
            title,
            isVisible,
            isMinimized,
            isCloaked,
            isForeground);
}

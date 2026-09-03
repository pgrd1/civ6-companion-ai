using Civ6Companion.App.Settings;
using Civ6Companion.Tests.TestSupport;
using FluentAssertions;

namespace Civ6Companion.Tests.Settings;

public sealed class JsonSettingsStoreTests
{
    [Fact]
    public async Task SaveThenLoad_PreservesAllSettings()
    {
        using var temp = new TempDirectory();
        var store = new JsonSettingsStore(temp.Path);
        var expected = new AppSettings("F8", 120, 80, 420, false, @"C:\Tools\codex.exe");

        await store.SaveAsync(expected, CancellationToken.None);
        var actual = await store.LoadAsync(CancellationToken.None);

        actual.Should().Be(expected);
    }

    [Fact]
    public async Task LoadAsync_WhenFileIsMissing_ReturnsDefaults()
    {
        using var temp = new TempDirectory();
        var store = new JsonSettingsStore(temp.Path);

        var settings = await store.LoadAsync(CancellationToken.None);

        settings.Should().Be(new AppSettings());
    }

    [Fact]
    public async Task LoadAsync_WhenJsonIsInvalid_PreservesTheFileAndReturnsDefaults()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "settings.json"), "not json");
        var store = new JsonSettingsStore(temp.Path);

        var settings = await store.LoadAsync(CancellationToken.None);

        settings.Should().Be(new AppSettings());
        Directory.GetFiles(temp.Path, "settings.json.invalid-*").Should().ContainSingle();
        File.Exists(Path.Combine(temp.Path, "settings.json")).Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_WhenConcurrentStoresReadOneInvalidFile_ReturnsDefaultsAndPreservesOneCopy()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "settings.json"), "not json");
        using var start = new ManualResetEventSlim(initialState: false);
        using var ready = new CountdownEvent(initialCount: 2);

        var loads = Enumerable.Range(0, 2)
            .Select(_ => Task.Factory.StartNew(
                async () =>
                {
                    ready.Signal();
                    start.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
                    return await new JsonSettingsStore(temp.Path).LoadAsync(CancellationToken.None);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap())
            .ToArray();

        ready.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        start.Set();
        var settings = await Task.WhenAll(loads).WaitAsync(TimeSpan.FromSeconds(5));
        var defaults = new AppSettings();

        settings.Should().OnlyContain(setting => setting == defaults);
        Directory.GetFiles(temp.Path, "settings.json.invalid-*").Should().ContainSingle();
        File.Exists(Path.Combine(temp.Path, "settings.json")).Should().BeFalse();
    }

    [Fact]
    public async Task LoadAsync_WhenConcurrentSaveReplacesInvalidSettings_LeavesTheSavedSettingsIntact()
    {
        using var temp = new TempDirectory();
        await File.WriteAllTextAsync(Path.Combine(temp.Path, "settings.json"), "not json");
        var expected = new AppSettings("F7", 12, 34, 460, true, @"C:\Tools\codex.exe");
        using var start = new ManualResetEventSlim(initialState: false);
        using var ready = new CountdownEvent(initialCount: 2);

        var load = Task.Factory.StartNew(
            async () =>
            {
                ready.Signal();
                start.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
                return await new JsonSettingsStore(temp.Path).LoadAsync(CancellationToken.None);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();
        var save = Task.Factory.StartNew(
            async () =>
            {
                ready.Signal();
                start.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
                await new JsonSettingsStore(temp.Path).SaveAsync(expected, CancellationToken.None);
            },
            CancellationToken.None,
            TaskCreationOptions.LongRunning,
            TaskScheduler.Default).Unwrap();

        ready.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        start.Set();
        await Task.WhenAll(load, save).WaitAsync(TimeSpan.FromSeconds(5));

        var actual = await new JsonSettingsStore(temp.Path).LoadAsync(CancellationToken.None);
        actual.Should().Be(expected);
        Directory.GetFiles(temp.Path, "settings.json.invalid-*").Length.Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public async Task SaveAsync_WhenSettingsAreInvalid_ThrowsUserSafeArgumentException()
    {
        using var temp = new TempDirectory();
        var store = new JsonSettingsStore(temp.Path);
        var invalid = new AppSettings(" ", 120, 80, -1, false, null);

        var action = () => store.SaveAsync(invalid, CancellationToken.None);

        await action.Should().ThrowAsync<ArgumentException>()
            .WithMessage("Settings contain invalid values.");
    }

    [Fact]
    public async Task LoadAsync_WhenCancellationIsRequested_PropagatesCancellation()
    {
        using var temp = new TempDirectory();
        var store = new JsonSettingsStore(temp.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => store.LoadAsync(cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SaveAsync_WhenCancellationIsRequested_PropagatesCancellation()
    {
        using var temp = new TempDirectory();
        var store = new JsonSettingsStore(temp.Path);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        var action = () => store.SaveAsync(new AppSettings(), cancellation.Token);

        await action.Should().ThrowAsync<OperationCanceledException>();
    }

    [Fact]
    public async Task SaveAsync_WhenConcurrentStoresShareADirectory_CompletesWithoutCorruption()
    {
        using var temp = new TempDirectory();
        using var start = new ManualResetEventSlim(initialState: false);
        using var ready = new CountdownEvent(initialCount: 16);
        var expectedSettings = Enumerable.Range(1, 16)
            .Select(index => new AppSettings(
                $"F{index}",
                index,
                index * 10,
                400 + index,
                index % 2 == 0,
                $@"C:\Tools\codex-{index}.exe"))
            .ToArray();

        var saves = expectedSettings
            .Select(settings => Task.Factory.StartNew(
                async () =>
                {
                    ready.Signal();
                    start.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
                    await new JsonSettingsStore(temp.Path).SaveAsync(settings, CancellationToken.None);
                },
                CancellationToken.None,
                TaskCreationOptions.LongRunning,
                TaskScheduler.Default).Unwrap())
            .ToArray();

        ready.Wait(TimeSpan.FromSeconds(2)).Should().BeTrue();
        start.Set();
        await Task.WhenAll(saves).WaitAsync(TimeSpan.FromSeconds(5));

        var actual = await new JsonSettingsStore(temp.Path).LoadAsync(CancellationToken.None);

        expectedSettings.Should().Contain(actual);
    }
}

using Civ6Companion.App.Advisor;
using Civ6Companion.App.Capture;
using Civ6Companion.Tests.TestSupport;
using FluentAssertions;

namespace Civ6Companion.Tests.Advisor;

public sealed class CaptureQueueTests
{
    [Fact]
    public async Task AddAsync_KeepsNewestSixInChronologicalOrderAndDeletesEvictedFile()
    {
        using var temp = new TempDirectory();
        await using var queue = new CaptureQueue(6);
        var paths = Enumerable.Range(1, 7).Select(index => CreateCapture(temp.Path, index)).ToArray();

        foreach (var capture in paths) await queue.AddAsync(capture);

        queue.Paths.Select(Path.GetFileName).Should().Equal(paths.Skip(1).Select(item => Path.GetFileName(item.Path)));
        File.Exists(paths[0].Path).Should().BeFalse();
        queue.Count.Should().Be(6);
    }

    [Fact]
    public async Task ClearAsync_DeletesAllQueuedFiles()
    {
        using var temp = new TempDirectory();
        await using var queue = new CaptureQueue(6);
        var captures = Enumerable.Range(1, 2).Select(index => CreateCapture(temp.Path, index)).ToArray();
        foreach (var capture in captures) await queue.AddAsync(capture);

        await queue.ClearAsync();

        queue.Count.Should().Be(0);
        captures.Should().OnlyContain(item => !File.Exists(item.Path));
    }

    private static TemporaryCapture CreateCapture(string root, int index)
    {
        var path = Path.Combine(root, $"{index}.png");
        File.WriteAllText(path, index.ToString());
        return new TemporaryCapture(path, keepScreenshots: false);
    }
}

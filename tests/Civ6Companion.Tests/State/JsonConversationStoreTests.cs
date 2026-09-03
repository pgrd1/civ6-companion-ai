using Civ6Companion.App.Common;
using Civ6Companion.App.State;
using Civ6Companion.Tests.TestSupport;
using FluentAssertions;

namespace Civ6Companion.Tests.State;

public sealed class JsonConversationStoreTests
{
    private static readonly DateTimeOffset Now = new(2026, 8, 3, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task AppendAsync_KeepsSixImmutableCappedMessages()
    {
        using var temp = new TempDirectory();
        var store = new JsonConversationStore(temp.Path, new FakeClock());
        var original = await store.StartNewGameAsync("일본", CancellationToken.None);
        var session = original;
        foreach (var i in Enumerable.Range(1, 8))
            session = await store.AppendAsync(session, new(MessageRole.User, $"메시지 {i}" + new string('x', 5_000), Now), CancellationToken.None);

        original.RecentMessages.Should().BeEmpty();
        session.RecentMessages.Should().HaveCount(6);
        session.RecentMessages[0].Text.Should().StartWith("메시지 3");
        session.RecentMessages.Should().OnlyContain(m => m.Text.Length <= 4_000);
        (await store.LoadCurrentAsync(CancellationToken.None))!.Id.Should().Be(session.Id);
    }

    [Fact]
    public async Task SaveAsync_CapsSummaryAndDoesNotLeaveTemporaryFiles()
    {
        using var temp = new TempDirectory();
        var store = new JsonConversationStore(temp.Path, new FakeClock());
        var session = await store.StartNewGameAsync("일본", CancellationToken.None);
        await store.SaveAsync(session with { CompressedSummary = new string('s', 7_000) }, CancellationToken.None);
        var loaded = await store.LoadCurrentAsync(CancellationToken.None);
        loaded!.CompressedSummary.Should().HaveLength(6_000);
        Directory.GetFiles(temp.Path, "*.tmp", SearchOption.AllDirectories).Should().BeEmpty();
    }

    [Fact]
    public async Task StartNewGame_ArchivesPreviousAndInvalidFileRecovers()
    {
        using var temp = new TempDirectory();
        var store = new JsonConversationStore(temp.Path, new FakeClock());
        var first = await store.StartNewGameAsync("일본", CancellationToken.None);
        var second = await store.StartNewGameAsync("한국", CancellationToken.None);
        Directory.GetFiles(Path.Combine(temp.Path, "Archive"), $"{first.Id:N}.json").Should().ContainSingle();
        second.Id.Should().NotBe(first.Id);

        await File.WriteAllTextAsync(Path.Combine(temp.Path, "current.json"), "not json");
        (await store.LoadCurrentAsync(CancellationToken.None)).Should().BeNull();
        Directory.GetFiles(temp.Path, "current.corrupt-*.json").Should().ContainSingle();
    }

    private sealed class FakeClock : IClock { public DateTimeOffset UtcNow => Now; }
}

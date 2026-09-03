using Civ6Companion.App.Advisor;
using Civ6Companion.App.Capture;
using Civ6Companion.App.Common;
using Civ6Companion.App.State;
using Civ6Companion.Tests.TestSupport;
using FluentAssertions;

namespace Civ6Companion.Tests.Advisor;

public sealed class AdvisorOrchestratorTests
{
    private static readonly CivWindow Window = new((nint)1, 1, new PixelRect(0, 0, 640, 360), "Civilization VI", true);

    [Fact]
    public async Task QueueThenAnalyze_SendsQueuedCaptureBeforeCurrentAndClearsAfterSuccess()
    {
        using var temp = new TempDirectory();
        var client = new RecordingClient();
        using var orchestrator = Create(temp.Path, client);

        await orchestrator.QueueCurrentScreenAsync(CancellationToken.None);
        Directory.EnumerateFiles(temp.Path).Should().ContainSingle();
        await orchestrator.AnalyzeCurrentScreenAsync(CancellationToken.None);

        client.Calls.Should().ContainSingle();
        client.Calls[0].Should().HaveCount(2);
        client.Calls[0][0].Should().NotBe(client.Calls[0][1]);
        Directory.EnumerateFiles(temp.Path).Should().BeEmpty();
    }

    [Fact]
    public async Task FailedAnalysis_PreservesQueuedCaptureForRetry()
    {
        using var temp = new TempDirectory();
        var client = new RecordingClient(failFirst: true);
        using var orchestrator = Create(temp.Path, client);

        await orchestrator.QueueCurrentScreenAsync(CancellationToken.None);
        await orchestrator.AnalyzeCurrentScreenAsync(CancellationToken.None);
        Directory.EnumerateFiles(temp.Path).Should().ContainSingle();

        await orchestrator.AnalyzeCurrentScreenAsync(CancellationToken.None);
        client.Calls.Should().HaveCount(2);
        client.Calls[1].Should().HaveCount(2);
        Directory.EnumerateFiles(temp.Path).Should().BeEmpty();
    }

    [Fact]
    public async Task StartNewGame_ClearsQueuedCapturesStartsDefaultSessionAndPublishesIdleConfirmation()
    {
        using var temp = new TempDirectory();
        var store = new MemoryStore();
        using var orchestrator = Create(temp.Path, new RecordingClient(), store);
        var states = new List<AdvisorState>();
        orchestrator.StateChanged += (_, state) => states.Add(state);

        await orchestrator.QueueCurrentScreenAsync(CancellationToken.None);
        var started = await orchestrator.StartNewGameAsync(CancellationToken.None);

        started.Should().BeTrue();
        Directory.EnumerateFiles(temp.Path).Should().BeEmpty();
        store.StartedCivilizations.Should().ContainSingle().Which.Should().Be("화면에서 자동 인식");
        states.Should().ContainSingle(state => state.Status == AdvisorStatus.Idle && state.Message!.Contains("새 게임"));
    }

    [Fact]
    public async Task StartNewGame_WhenSessionResetFails_ReturnsFalseAndPublishesAnError()
    {
        using var temp = new TempDirectory();
        var store = new MemoryStore { StartNewGameException = new IOException("disk unavailable") };
        using var orchestrator = Create(temp.Path, new RecordingClient(), store);
        var states = new List<AdvisorState>();
        orchestrator.StateChanged += (_, state) => states.Add(state);

        await orchestrator.QueueCurrentScreenAsync(CancellationToken.None);
        var started = await orchestrator.StartNewGameAsync(CancellationToken.None);

        started.Should().BeFalse();
        states.Should().ContainSingle(state => state.Status == AdvisorStatus.Error && state.ErrorCode == "NEW_GAME_FAILED");
        Directory.EnumerateFiles(temp.Path).Should().ContainSingle();
    }

    [Fact]
    public async Task FailedAnalysis_DoesNotExposeCliErrorDetailsInPublishedState()
    {
        using var temp = new TempDirectory();
        using var orchestrator = Create(temp.Path, new RecordingClient(analysisException:
            new CodexClientException("CODEX_FAILED", "C:\\Users\\player\\.codex token prompt fragment")));
        var states = new List<AdvisorState>();
        orchestrator.StateChanged += (_, state) => states.Add(state);

        await orchestrator.AnalyzeCurrentScreenAsync(CancellationToken.None);

        var error = states.Single(state => state.Status == AdvisorStatus.Error);
        error.RawFallback.Should().BeNull();
        error.Message.Should().NotContain("token").And.NotContain("prompt fragment");
    }

    [Fact]
    public async Task UnexpectedChatFailure_PublishesSafeErrorStateInsteadOfThrowing()
    {
        using var temp = new TempDirectory();
        using var orchestrator = Create(temp.Path, new RecordingClient(chatException:
            new InvalidOperationException("C:\\private\\prompt auth details")));
        var states = new List<AdvisorState>();
        orchestrator.StateChanged += (_, state) => states.Add(state);

        await orchestrator.SendChatAsync("무엇을 해야 하나요?", CancellationToken.None);

        var error = states.Single(state => state.Status == AdvisorStatus.Error);
        error.ErrorCode.Should().Be("CHAT_FAILED");
        error.RawFallback.Should().BeNull();
        error.Message.Should().NotContain("private").And.NotContain("auth");
    }

    [Fact]
    public async Task Dispose_CancelsActiveChatWaitsForReleaseAndIsIdempotent()
    {
        using var temp = new TempDirectory();
        var client = new BlockingChatClient();
        var orchestrator = Create(temp.Path, client);

        var chat = orchestrator.SendChatAsync("계속 진행해도 될까요?", CancellationToken.None);
        await client.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var disposing = Task.Run(orchestrator.Dispose);

        await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        await chat;
        var secondDispose = () => orchestrator.Dispose();
        secondDispose.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_CancelsActiveQueueWaitsForReleaseAndIsIdempotent()
    {
        using var temp = new TempDirectory();
        var locator = new BlockingLocator();
        var orchestrator = Create(temp.Path, new RecordingClient(), locator: locator);

        var queue = Task.Run(() => orchestrator.QueueCurrentScreenAsync(CancellationToken.None));
        await locator.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var disposing = Task.Run(orchestrator.Dispose);

        await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        await queue;
        var secondDispose = () => orchestrator.Dispose();
        secondDispose.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_CancelsActiveAnalysisWaitsForReleaseAndIsIdempotent()
    {
        using var temp = new TempDirectory();
        var locator = new BlockingLocator();
        var orchestrator = Create(temp.Path, new RecordingClient(), locator: locator);

        var analysis = orchestrator.AnalyzeCurrentScreenAsync(CancellationToken.None);
        await locator.Started.Task.WaitAsync(TimeSpan.FromSeconds(2));
        var disposing = Task.Run(orchestrator.Dispose);

        await disposing.WaitAsync(TimeSpan.FromSeconds(2));
        await analysis;
        var secondDispose = () => orchestrator.Dispose();
        secondDispose.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_FromSynchronousStateChangedHandlerDefersCleanupUntilQueueReleasesGate()
    {
        using var temp = new TempDirectory();
        var handlerReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var orchestrator = Create(temp.Path, new RecordingClient());
        orchestrator.StateChanged += (_, state) =>
        {
            if (state.Status != AdvisorStatus.Capturing) return;
            orchestrator.Dispose();
            handlerReturned.TrySetResult();
        };

        var queue = Task.Run(() => orchestrator.QueueCurrentScreenAsync(CancellationToken.None));

        await handlerReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));
        await queue.WaitAsync(TimeSpan.FromSeconds(2));
        var secondDispose = () => orchestrator.Dispose();
        secondDispose.Should().NotThrow();
    }

    [Fact]
    public async Task Dispose_AfterAsyncStateChangedContinuationFinalizesQueuedCaptureCleanup()
    {
        using var temp = new TempDirectory();
        var queueFinished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var disposeReturned = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var orchestrator = Create(temp.Path, new RecordingClient());
        orchestrator.StateChanged += async (_, state) =>
        {
            if (state.Status != AdvisorStatus.Capturing) return;
            await queueFinished.Task;
            orchestrator.Dispose();
            disposeReturned.TrySetResult();
        };

        await orchestrator.QueueCurrentScreenAsync(CancellationToken.None);
        Directory.EnumerateFiles(temp.Path).Should().ContainSingle();
        queueFinished.TrySetResult();
        await disposeReturned.Task.WaitAsync(TimeSpan.FromSeconds(2));

        Directory.EnumerateFiles(temp.Path).Should().BeEmpty();
    }

    [Fact]
    public async Task Dispose_PreventsSubsequentOperationsFromStarting()
    {
        using var temp = new TempDirectory();
        var locator = new BlockingLocator();
        var client = new BlockingChatClient();
        var store = new MemoryStore();
        var orchestrator = Create(temp.Path, client, store, locator);

        orchestrator.Dispose();
        await orchestrator.QueueCurrentScreenAsync(CancellationToken.None);
        await orchestrator.AnalyzeCurrentScreenAsync(CancellationToken.None);
        await orchestrator.SendChatAsync("계속 진행해도 될까요?", CancellationToken.None);
        var started = await orchestrator.StartNewGameAsync(CancellationToken.None);

        started.Should().BeFalse();
        locator.Started.Task.IsCompleted.Should().BeFalse();
        client.Started.Task.IsCompleted.Should().BeFalse();
        store.StartedCivilizations.Should().BeEmpty();
    }

    private static AdvisorOrchestrator Create(string captureRoot, ICodexCliClient client, MemoryStore? store = null,
        ICivWindowLocator? locator = null) => new(
        locator ?? new FoundLocator(),
        new CompositeCaptureService(new StubCaptureBackend("civ-map-sample.png"),
            new StubCaptureBackend("black-frame.png"), captureRoot),
        client, new PromptBuilder(), store ?? new MemoryStore(), new FixedClock());

    private sealed class FoundLocator : ICivWindowLocator
    {
        public Task<WindowLookupResult> FindAsync(CancellationToken cancellationToken) =>
            Task.FromResult(WindowLookupResult.Found(Window));
    }

    private sealed class BlockingLocator : ICivWindowLocator
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<WindowLookupResult> FindAsync(CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation token should end this operation.");
        }
    }

    private sealed class RecordingClient(
        bool failFirst = false,
        Exception? analysisException = null,
        Exception? chatException = null) : ICodexCliClient
    {
        public List<IReadOnlyList<string>> Calls { get; } = [];

        public Task<AnalysisResponse> AnalyzeAsync(IReadOnlyList<string> imagePaths, string prompt, CancellationToken cancellationToken)
        {
            Calls.Add(imagePaths.ToArray());
            if (failFirst && Calls.Count == 1) throw new CodexClientException("CODEX_FAILED", "fail");
            if (analysisException is not null) throw analysisException;
            return Task.FromResult(new AnalysisResponse(ScreenType.Map, 1, "ok", [], [], [], [], null, "state"));
        }

        public Task<ChatResponse> ChatAsync(string prompt, CancellationToken cancellationToken)
        {
            if (chatException is not null) throw chatException;
            return Task.FromResult(new ChatResponse("ok", null, "state"));
        }
    }

    private sealed class BlockingChatClient : ICodexCliClient
    {
        public TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<AnalysisResponse> AnalyzeAsync(IReadOnlyList<string> imagePaths, string prompt, CancellationToken cancellationToken) =>
            Task.FromResult(new AnalysisResponse(ScreenType.Map, 1, "ok", [], [], [], [], null, "state"));

        public async Task<ChatResponse> ChatAsync(string prompt, CancellationToken cancellationToken)
        {
            Started.SetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            throw new InvalidOperationException("The cancellation token should end this operation.");
        }
    }

    private sealed class MemoryStore : IConversationStore
    {
        private GameSession? _session;
        public List<string> StartedCivilizations { get; } = [];
        public Exception? StartNewGameException { get; init; }
        public Task<GameSession?> LoadCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(_session);
        public Task SaveAsync(GameSession session, CancellationToken cancellationToken) { _session = session; return Task.CompletedTask; }
        public Task<GameSession> StartNewGameAsync(string civilization, CancellationToken cancellationToken)
        {
            if (StartNewGameException is not null) throw StartNewGameException;
            StartedCivilizations.Add(civilization);
            _session = new GameSession(Guid.NewGuid(), DateTimeOffset.UnixEpoch, civilization, "?", "?", "?", "", null, []);
            return Task.FromResult(_session);
        }
        public Task<GameSession> AppendAsync(GameSession session, ConversationMessage message, CancellationToken cancellationToken) =>
            Task.FromResult(session with { RecentMessages = [.. session.RecentMessages, message] });
    }

    private sealed class FixedClock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.UnixEpoch; }
}

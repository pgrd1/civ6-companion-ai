using Civ6Companion.App.Capture;
using Civ6Companion.App.Common;
using Civ6Companion.App.State;

namespace Civ6Companion.App.Advisor;

public sealed class AdvisorOrchestrator : IAdvisorOrchestrator, IDisposable
{
    private const string DefaultCivilization = "화면에서 자동 인식";
    private const int Running = 0;
    private const int DisposeRequested = 1;
    private const int FinalizingDispose = 2;
    private const int Disposed = 3;
    [ThreadStatic]
    private static AdvisorOrchestrator? _currentPublisher;

    private readonly ICivWindowLocator _locator;
    private readonly CompositeCaptureService _capture;
    private readonly ICodexCliClient _client;
    private readonly PromptBuilder _prompts;
    private readonly IConversationStore _store;
    private readonly IClock _clock;
    private readonly CaptureQueue _queuedCaptures = new(6);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly object _lifecycle = new();
    private readonly TaskCompletionSource _disposeCompletion = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private CancellationTokenSource? _active;
    private int _disposeState;

    public AdvisorOrchestrator(ICivWindowLocator locator, CompositeCaptureService capture, ICodexCliClient client,
        PromptBuilder prompts, IConversationStore store, IClock clock)
    {
        _locator = locator; _capture = capture; _client = client; _prompts = prompts; _store = store; _clock = clock;
    }

    public event EventHandler<AdvisorState>? StateChanged;

    public async Task QueueCurrentScreenAsync(CancellationToken cancellationToken)
    {
        var operation = await BeginOperationAsync(cancellationToken, TimeSpan.FromSeconds(20), waitForGate: false).ConfigureAwait(false);
        if (operation is null)
        {
            Publish(new(AdvisorStatus.Capturing, Message: "다른 작업이 끝난 뒤 F7을 다시 누르세요."));
            return;
        }
        try
        {
            Publish(new(AdvisorStatus.Capturing, Message: "분석할 화면을 저장하는 중…"));
            var lookup = await _locator.FindAsync(operation.Token).ConfigureAwait(false);
            if (lookup.Window is null)
            {
                Publish(WindowError(lookup.Failure));
                return;
            }

            var capture = await _capture.CaptureAsync(lookup.Window, operation.Token).ConfigureAwait(false);
            await _queuedCaptures.AddAsync(capture).ConfigureAwait(false);
            Publish(new(AdvisorStatus.Idle,
                Message: $"화면 저장됨 ({_queuedCaptures.Count}/6). 더 저장하려면 F7, 모두 분석하려면 F8을 누르세요."));
        }
        catch (OperationCanceledException)
        {
            Publish(new(AdvisorStatus.Idle, Message: "화면 저장을 취소했습니다."));
        }
        catch (Exception)
        {
            Publish(new(AdvisorStatus.Error, Message: "화면을 저장하지 못했습니다. 문명 6 창을 확인하세요.", ErrorCode: "CAPTURE_FAILED"));
        }
        finally
        {
            CompleteOperation(operation);
        }
    }

    public async Task AnalyzeCurrentScreenAsync(CancellationToken cancellationToken)
    {
        var operation = await BeginOperationAsync(cancellationToken, TimeSpan.FromSeconds(70), waitForGate: false).ConfigureAwait(false);
        if (operation is null)
        {
            Publish(new(AdvisorStatus.Analyzing, Message: "이미 분석 중입니다."));
            return;
        }
        try
        {
            Publish(new(AdvisorStatus.Capturing, Message: "문명 6 화면을 캡처하는 중…"));
            var lookup = await _locator.FindAsync(operation.Token).ConfigureAwait(false);
            if (lookup.Window is null)
            {
                Publish(WindowError(lookup.Failure));
                return;
            }

            await using var image = await _capture.CaptureAsync(lookup.Window, operation.Token).ConfigureAwait(false);
            var session = await GetSessionAsync(operation.Token).ConfigureAwait(false);
            var imagePaths = _queuedCaptures.Paths.Append(image.Path).ToArray();
            Publish(new(AdvisorStatus.Analyzing, Message: $"Codex가 화면 {imagePaths.Length}장을 함께 분석하는 중…"));
            var response = await _client.AnalyzeAsync(imagePaths,
                _prompts.BuildAnalysisPrompt(session, imagePaths.Length), operation.Token).ConfigureAwait(false);
            var updated = session with { LastAnalysis = response, CompressedSummary = response.StateUpdate };
            await _store.SaveAsync(updated, operation.Token).ConfigureAwait(false);
            await _queuedCaptures.ClearAsync().ConfigureAwait(false);
            Publish(new(AdvisorStatus.Ready, Analysis: response));
        }
        catch (OperationCanceledException)
        {
            Publish(new(AdvisorStatus.Idle, Message: "분석을 취소했습니다."));
        }
        catch (CodexClientException ex)
        {
            Publish(new(AdvisorStatus.Error, Message: KoreanMessage(ex.Code), ErrorCode: ex.Code));
        }
        catch (Exception)
        {
            Publish(new(AdvisorStatus.Error, Message: "분석하지 못했습니다. 게임 창과 Codex를 확인하세요.", ErrorCode: "ANALYSIS_FAILED"));
        }
        finally
        {
            CompleteOperation(operation);
        }
    }

    public async Task<bool> StartNewGameAsync(CancellationToken cancellationToken)
    {
        ActiveOperation? operation;
        try
        {
            operation = await BeginOperationAsync(cancellationToken, timeout: null, waitForGate: true).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Publish(new(AdvisorStatus.Idle, Message: "새 게임 시작을 취소했습니다."));
            return false;
        }

        if (operation is null) return false;
        try
        {
            await _store.StartNewGameAsync(DefaultCivilization, operation.Token).ConfigureAwait(false);
            await _queuedCaptures.ClearAsync().ConfigureAwait(false);
            Publish(new(AdvisorStatus.Idle, Message: "새 게임을 시작했습니다. 화면을 저장하려면 F7을 누르세요."));
            return true;
        }
        catch (OperationCanceledException)
        {
            Publish(new(AdvisorStatus.Idle, Message: "새 게임 시작을 취소했습니다."));
            return false;
        }
        catch (Exception)
        {
            Publish(new(AdvisorStatus.Error, Message: "새 게임을 시작하지 못했습니다.", ErrorCode: "NEW_GAME_FAILED"));
            return false;
        }
        finally
        {
            CompleteOperation(operation);
        }
    }

    public async Task SendChatAsync(string message, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(message)) return;
        var operation = await BeginOperationAsync(cancellationToken, TimeSpan.FromSeconds(70), waitForGate: false).ConfigureAwait(false);
        if (operation is null) return;
        try
        {
            var session = await GetSessionAsync(operation.Token).ConfigureAwait(false);
            session = await _store.AppendAsync(session, new(MessageRole.User, message.Trim(), _clock.UtcNow), operation.Token).ConfigureAwait(false);
            Publish(new(AdvisorStatus.Analyzing, Message: "답변을 준비하는 중…"));
            var response = await _client.ChatAsync(_prompts.BuildChatPrompt(session, message.Trim()), operation.Token).ConfigureAwait(false);
            session = await _store.AppendAsync(session, new(MessageRole.Assistant, response.Message, _clock.UtcNow), operation.Token).ConfigureAwait(false);
            session = session with { CompressedSummary = response.StateUpdate };
            await _store.SaveAsync(session, operation.Token).ConfigureAwait(false);
            Publish(new(AdvisorStatus.Ready, Chat: response));
        }
        catch (OperationCanceledException) { Publish(new(AdvisorStatus.Idle, Message: "요청을 취소했습니다.")); }
        catch (CodexClientException ex)
        {
            Publish(new(AdvisorStatus.Error, Message: KoreanMessage(ex.Code), ErrorCode: ex.Code));
        }
        catch (Exception)
        {
            Publish(new(AdvisorStatus.Error, Message: "답변을 준비하지 못했습니다. 잠시 후 다시 시도하세요.", ErrorCode: "CHAT_FAILED"));
        }
        finally { CompleteOperation(operation); }
    }

    public void Cancel()
    {
        CancellationTokenSource? active;
        lock (_lifecycle) active = _active;
        TryCancel(active);
    }

    public void Dispose()
    {
        RequestDisposal();
        if (ReferenceEquals(_currentPublisher, this)) return;
        FinalizeDisposal();
    }

    private void RequestDisposal()
    {
        if (Interlocked.CompareExchange(ref _disposeState, DisposeRequested, Running) != Running) return;

        CancellationTokenSource? active;
        lock (_lifecycle) active = _active;
        TryCancel(active);
    }

    private void FinalizeDisposal()
    {
        while (true)
        {
            var state = Volatile.Read(ref _disposeState);
            if (state == Disposed) return;
            if (state == FinalizingDispose)
            {
                _disposeCompletion.Task.GetAwaiter().GetResult();
                return;
            }
            if (Interlocked.CompareExchange(ref _disposeState, FinalizingDispose, DisposeRequested) == DisposeRequested) break;
        }

        Exception? failure = null;
        try
        {
            _gate.Wait();
            try
            {
                _queuedCaptures.DisposeAsync().AsTask().GetAwaiter().GetResult();
            }
            finally
            {
                _gate.Dispose();
            }
        }
        catch (Exception ex)
        {
            failure = ex;
            throw;
        }
        finally
        {
            Volatile.Write(ref _disposeState, Disposed);
            if (failure is null) _disposeCompletion.TrySetResult();
            else _disposeCompletion.TrySetException(failure);
        }
    }

    private async Task<GameSession> GetSessionAsync(CancellationToken token) =>
        await _store.LoadCurrentAsync(token).ConfigureAwait(false) ?? await _store.StartNewGameAsync(DefaultCivilization, token).ConfigureAwait(false);

    private async Task<ActiveOperation?> BeginOperationAsync(CancellationToken cancellationToken, TimeSpan? timeout, bool waitForGate)
    {
        if (waitForGate)
        {
            Task wait;
            lock (_lifecycle)
            {
                if (Volatile.Read(ref _disposeState) != Running) return null;
                wait = _gate.WaitAsync(cancellationToken);
            }
            await wait.ConfigureAwait(false);
        }
        else
        {
            Task<bool> wait;
            lock (_lifecycle)
            {
                if (Volatile.Read(ref _disposeState) != Running) return null;
                wait = _gate.WaitAsync(0, cancellationToken);
            }
            if (!await wait.ConfigureAwait(false)) return null;
        }
        lock (_lifecycle)
        {
            if (Volatile.Read(ref _disposeState) != Running)
            {
                _gate.Release();
                return null;
            }

            try
            {
                var operation = new ActiveOperation(cancellationToken, timeout);
                _active = operation.Source;
                return operation;
            }
            catch
            {
                _gate.Release();
                throw;
            }
        }
    }

    private void CompleteOperation(ActiveOperation operation)
    {
        lock (_lifecycle)
        {
            if (ReferenceEquals(_active, operation.Source)) _active = null;
        }
        operation.Dispose();
        _gate.Release();
        if (Volatile.Read(ref _disposeState) == DisposeRequested) FinalizeDisposal();
    }

    private static void TryCancel(CancellationTokenSource? source)
    {
        try { source?.Cancel(); }
        catch (ObjectDisposedException) { }
    }

    private void Publish(AdvisorState state)
    {
        var previous = _currentPublisher;
        _currentPublisher = this;
        try { StateChanged?.Invoke(this, state); }
        finally { _currentPublisher = previous; }
    }

    private static AdvisorState WindowError(WindowLookupFailure? failure) => failure switch
    {
        WindowLookupFailure.NotForeground => new(AdvisorStatus.Error, Message: "문명 6 창을 한 번 클릭한 뒤 F8을 누르세요.", ErrorCode: "CIV_NOT_FOREGROUND"),
        WindowLookupFailure.Minimized => new(AdvisorStatus.Error, Message: "문명 6 창을 복원한 뒤 다시 시도하세요.", ErrorCode: "CIV_MINIMIZED"),
        _ => new(AdvisorStatus.Error, Message: "실행 중인 문명 6 창을 찾지 못했습니다.", ErrorCode: "CIV_NOT_RUNNING")
    };

    private static string KoreanMessage(string code) => code switch
    {
        "CODEX_MISSING" => "Codex CLI를 찾지 못했습니다. Codex를 설치하거나 경로를 설정하세요.",
        "CODEX_TIMEOUT" => "Codex 응답 시간이 초과되었습니다. 잠시 후 다시 시도하세요.",
        "CODEX_BAD_RESPONSE" => "화면을 정확히 읽지 못했습니다. 다른 화면에서 다시 F8을 누르세요.",
        _ => "Codex 실행에 실패했습니다. 잠시 후 다시 시도하세요."
    };

    private sealed class ActiveOperation : IDisposable
    {
        private readonly CancellationTokenSource? _timeout;

        public ActiveOperation(CancellationToken cancellationToken, TimeSpan? timeout)
        {
            _timeout = timeout is { } duration ? new CancellationTokenSource(duration) : null;
            Source = _timeout is null
                ? CancellationTokenSource.CreateLinkedTokenSource(cancellationToken)
                : CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _timeout.Token);
        }

        public CancellationTokenSource Source { get; }
        public CancellationToken Token => Source.Token;

        public void Dispose()
        {
            Source.Dispose();
            _timeout?.Dispose();
        }
    }
}

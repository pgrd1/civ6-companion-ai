using System.Windows;
using Civ6Companion.App.Advisor;
using Civ6Companion.App.Common;

namespace Civ6Companion.App.Shell;

public sealed record ImmediateActionView(string Action, string Reason);

public sealed class OverlayViewModel : ObservableObject, IDisposable
{
    private readonly IAdvisorOrchestrator _advisor;
    private string _screenBadge = "대기";
    private string _title = "문명 6 도우미";
    private string _statusMessage = "F8을 누르면 현재 화면을 분석합니다.";
    private string _chatInput = "";
    private string? _needsAnotherScreen;
    private string? _rawFallback;
    private bool _isBusy;
    private IReadOnlyList<ImmediateActionView> _immediateActions = [];
    private IReadOnlyList<string> _nextSteps = [];
    private IReadOnlyList<string> _warnings = [];
    private IReadOnlyList<string> _fiveTurnGoals = [];
    private IReadOnlyList<string> _chatTranscript = [];

    public OverlayViewModel(IAdvisorOrchestrator advisor)
    {
        _advisor = advisor ?? throw new ArgumentNullException(nameof(advisor));
        QueueCaptureCommand = new(token => _advisor.QueueCurrentScreenAsync(token), () => !IsBusy);
        AnalyzeCommand = new(token => _advisor.AnalyzeCurrentScreenAsync(token), () => !IsBusy);
        SendChatCommand = new(SendChatAsync, () => !IsBusy && !string.IsNullOrWhiteSpace(ChatInput));
        CancelCommand = new(() => _advisor.Cancel(), () => IsBusy);
        HideCommand = new(() => HideRequested?.Invoke(this, EventArgs.Empty));
        NewGameCommand = new(StartNewGameAsync, () => !IsBusy);
        RequestNewGameCommand = new(() => NewGameRequested?.Invoke(this, EventArgs.Empty), () => NewGameCommand.CanExecute(null));
        ExitCommand = new(() => ExitRequested?.Invoke(this, EventArgs.Empty));
        NewGameCommand.CanExecuteChanged += OnNewGameCanExecuteChanged;
        _advisor.StateChanged += OnAdvisorStateChanged;
    }

    public event EventHandler? HideRequested;
    public event EventHandler? NewGameRequested;
    public event EventHandler? ExitRequested;

    public AsyncCommand QueueCaptureCommand { get; }
    public AsyncCommand AnalyzeCommand { get; }
    public AsyncCommand SendChatCommand { get; }
    public RelayCommand CancelCommand { get; }
    public RelayCommand HideCommand { get; }
    public AsyncCommand NewGameCommand { get; }
    public RelayCommand RequestNewGameCommand { get; }
    public RelayCommand ExitCommand { get; }

    public string ScreenBadge { get => _screenBadge; private set => SetProperty(ref _screenBadge, value); }
    public string Title { get => _title; private set => SetProperty(ref _title, value); }
    public string StatusMessage { get => _statusMessage; private set => SetProperty(ref _statusMessage, value); }
    public string ChatInput
    {
        get => _chatInput;
        set
        {
            if (SetProperty(ref _chatInput, value)) SendChatCommand.RaiseCanExecuteChanged();
        }
    }
    public string? NeedsAnotherScreen { get => _needsAnotherScreen; private set => SetProperty(ref _needsAnotherScreen, value); }
    public string? RawFallback { get => _rawFallback; private set => SetProperty(ref _rawFallback, value); }
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (!SetProperty(ref _isBusy, value)) return;
            QueueCaptureCommand.RaiseCanExecuteChanged();
            AnalyzeCommand.RaiseCanExecuteChanged();
            SendChatCommand.RaiseCanExecuteChanged();
            CancelCommand.RaiseCanExecuteChanged();
            NewGameCommand.RaiseCanExecuteChanged();
            RequestNewGameCommand.RaiseCanExecuteChanged();
        }
    }
    public IReadOnlyList<ImmediateActionView> ImmediateActions { get => _immediateActions; private set => SetProperty(ref _immediateActions, value); }
    public IReadOnlyList<string> NextSteps { get => _nextSteps; private set => SetProperty(ref _nextSteps, value); }
    public IReadOnlyList<string> Warnings { get => _warnings; private set => SetProperty(ref _warnings, value); }
    public IReadOnlyList<string> FiveTurnGoals { get => _fiveTurnGoals; private set => SetProperty(ref _fiveTurnGoals, value); }
    public IReadOnlyList<string> ChatTranscript { get => _chatTranscript; private set => SetProperty(ref _chatTranscript, value); }

    public void Dispose()
    {
        _advisor.StateChanged -= OnAdvisorStateChanged;
        NewGameCommand.CanExecuteChanged -= OnNewGameCanExecuteChanged;
    }

    private async Task SendChatAsync(CancellationToken cancellationToken)
    {
        var message = ChatInput.Trim();
        if (message.Length == 0) return;
        ChatInput = "";
        ChatTranscript = [.. ChatTranscript, $"나: {message}"];
        await _advisor.SendChatAsync(message, cancellationToken).ConfigureAwait(true);
    }

    private async Task StartNewGameAsync(CancellationToken cancellationToken)
    {
        var started = await _advisor.StartNewGameAsync(cancellationToken).ConfigureAwait(true);
        if (started) ClearForNewSession();
    }

    private void OnAdvisorStateChanged(object? sender, AdvisorState state)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher is not null && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(() => ApplyState(state));
            return;
        }

        ApplyState(state);
    }

    private void ApplyState(AdvisorState state)
    {
        IsBusy = state.Status is AdvisorStatus.Capturing or AdvisorStatus.Analyzing;
        if (!string.IsNullOrWhiteSpace(state.Message)) StatusMessage = state.Message;
        RawFallback = state.RawFallback;

        if (state.Analysis is { } analysis)
        {
            ScreenBadge = BadgeFor(analysis.ScreenType);
            Title = analysis.Title;
            StatusMessage = "분석 완료";
            ImmediateActions = analysis.ImmediateActions.Take(3)
                .Select(action => new ImmediateActionView(action.Action, action.Reason)).ToArray();
            NextSteps = analysis.NextSteps.ToArray();
            Warnings = analysis.Warnings.ToArray();
            FiveTurnGoals = analysis.FiveTurnGoals.ToArray();
            NeedsAnotherScreen = analysis.NeedsAnotherScreen;
        }

        if (state.Chat is { } chat)
        {
            ChatTranscript = [.. ChatTranscript, $"도우미: {chat.Message}"];
            NeedsAnotherScreen = chat.NeedsAnotherScreen;
        }
    }

    private void ClearForNewSession()
    {
        ScreenBadge = "대기";
        Title = "문명 6 도우미";
        StatusMessage = "새 게임을 시작했습니다. 화면을 저장하려면 F7을 누르세요.";
        ChatInput = "";
        ImmediateActions = [];
        NextSteps = [];
        Warnings = [];
        FiveTurnGoals = [];
        NeedsAnotherScreen = null;
        RawFallback = null;
        ChatTranscript = [];
    }

    private void OnNewGameCanExecuteChanged(object? sender, EventArgs eventArgs) =>
        RequestNewGameCommand.RaiseCanExecuteChanged();

    private static string BadgeFor(ScreenType screenType) => screenType switch
    {
        ScreenType.Map => "지도",
        ScreenType.CityProduction => "생산",
        ScreenType.Technology => "기술",
        ScreenType.Civic => "사회제도",
        ScreenType.Government => "정부·정책",
        ScreenType.GreatPerson => "위인",
        ScreenType.CityState => "도시국가",
        ScreenType.Diplomacy => "외교",
        ScreenType.Trade => "교역",
        ScreenType.CitizenManagement => "시민 관리",
        ScreenType.Religion => "종교",
        _ => "기타",
    };
}

namespace Civ6Companion.App.Advisor;

public enum AdvisorStatus { Idle, Capturing, Analyzing, Ready, Error }

public sealed record AdvisorState(
    AdvisorStatus Status,
    AnalysisResponse? Analysis = null,
    ChatResponse? Chat = null,
    string? Message = null,
    string? ErrorCode = null,
    string? RawFallback = null);

public interface IAdvisorOrchestrator
{
    event EventHandler<AdvisorState>? StateChanged;
    Task QueueCurrentScreenAsync(CancellationToken cancellationToken);
    Task AnalyzeCurrentScreenAsync(CancellationToken cancellationToken);
    Task<bool> StartNewGameAsync(CancellationToken cancellationToken);
    Task SendChatAsync(string message, CancellationToken cancellationToken);
    void Cancel();
}

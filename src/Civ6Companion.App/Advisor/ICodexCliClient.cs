namespace Civ6Companion.App.Advisor;

public interface ICodexCliClient
{
    Task<AnalysisResponse> AnalyzeAsync(IReadOnlyList<string> imagePaths, string prompt, CancellationToken cancellationToken);
    Task<ChatResponse> ChatAsync(string prompt, CancellationToken cancellationToken);
}

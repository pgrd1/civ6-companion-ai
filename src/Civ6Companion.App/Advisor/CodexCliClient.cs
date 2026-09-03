using System.IO;

namespace Civ6Companion.App.Advisor;

public sealed class CodexCliClient : ICodexCliClient
{
    private readonly IProcessRunner _runner;
    private readonly IExecutableLocator _locator;
    private readonly string _analysisSchema;
    private readonly string _chatSchema;
    private readonly string _workingDirectory;
    private readonly string? _configuredPath;

    public CodexCliClient(IProcessRunner runner, IExecutableLocator locator, string analysisSchema,
        string chatSchema, string workingDirectory, string? configuredPath = null)
    {
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _locator = locator ?? throw new ArgumentNullException(nameof(locator));
        _analysisSchema = Path.GetFullPath(analysisSchema);
        _chatSchema = Path.GetFullPath(chatSchema);
        _workingDirectory = Path.GetFullPath(workingDirectory);
        _configuredPath = configuredPath;
    }

    public async Task<AnalysisResponse> AnalyzeAsync(IReadOnlyList<string> imagePaths, string prompt, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(imagePaths);
        if (imagePaths.Count == 0) throw new ArgumentException("At least one image is required.", nameof(imagePaths));
        var resolvedPaths = imagePaths.Select(path => Path.GetFullPath(
            string.IsNullOrWhiteSpace(path) ? throw new ArgumentException("Image paths cannot be empty.", nameof(imagePaths)) : path)).ToArray();
        var json = await InvokeAsync(resolvedPaths, _analysisSchema, prompt, cancellationToken).ConfigureAwait(false);
        var parsed = AnalysisResponseValidator.Parse(json);
        if (!parsed.IsValid || parsed.Value is null)
            throw new CodexClientException("CODEX_BAD_RESPONSE", string.Join("; ", parsed.Errors));
        return parsed.Value;
    }

    public async Task<ChatResponse> ChatAsync(string prompt, CancellationToken cancellationToken)
    {
        var json = await InvokeAsync([], _chatSchema, prompt, cancellationToken).ConfigureAwait(false);
        var parsed = AnalysisResponseValidator.ParseChat(json);
        if (!parsed.IsValid || parsed.Value is null)
            throw new CodexClientException("CODEX_BAD_RESPONSE", string.Join("; ", parsed.Errors));
        return parsed.Value;
    }

    private async Task<string> InvokeAsync(IReadOnlyList<string> imagePaths, string schemaPath, string prompt, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(prompt);
        var executable = _locator.Find(_configuredPath)
            ?? throw new CodexClientException("CODEX_MISSING", "Codex CLI를 찾지 못했습니다. Codex를 설치하거나 경로를 설정하세요.");
        Directory.CreateDirectory(_workingDirectory);
        var resultPath = Path.Combine(_workingDirectory, $"result-{Guid.NewGuid():N}.json");
        try
        {
            var arguments = new List<string>
            {
                "exec",
                "--ignore-user-config",
                "--ignore-rules",
                "--ephemeral",
                "--model", "gpt-5.6-sol",
                "--config", "model_reasoning_effort=\"low\"",
                "--sandbox", "read-only",
                "--skip-git-repo-check"
            };
            foreach (var imagePath in imagePaths)
            {
                arguments.Add("--image");
                arguments.Add(imagePath);
            }
            arguments.Add("--output-schema");
            arguments.Add(schemaPath);
            arguments.Add("--output-last-message");
            arguments.Add(resultPath);
            arguments.Add("-");

            var result = await _runner.RunAsync(new ProcessRequest(executable, arguments, _workingDirectory,
                prompt, TimeSpan.FromSeconds(25)), cancellationToken).ConfigureAwait(false);
            if (result.TimedOut) throw new CodexClientException("CODEX_TIMEOUT", "Codex 응답 시간이 초과되었습니다.");
            if (result.ExitCode != 0) throw new CodexClientException("CODEX_FAILED", SafeError());
            if (!File.Exists(resultPath)) throw new CodexClientException("CODEX_BAD_RESPONSE", "Codex 결과 파일이 없습니다.");
            return await File.ReadAllTextAsync(resultPath, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            try { File.Delete(resultPath); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }

    private static string SafeError() => "Codex 실행에 실패했습니다.";
}

public sealed class CodexClientException(string code, string message) : Exception(message)
{
    public string Code { get; } = code;
}

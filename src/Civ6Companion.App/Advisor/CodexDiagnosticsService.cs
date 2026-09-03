namespace Civ6Companion.App.Advisor;

public sealed record CodexDiagnosticsResult(
    string? ExecutablePath,
    bool IsInstalled,
    bool IsLoggedIn,
    string Message);

public sealed class CodexDiagnosticsService
{
    private static readonly TimeSpan LoginStatusTimeout = TimeSpan.FromSeconds(15);

    private readonly IProcessRunner _processRunner;
    private readonly IExecutableLocator _executableLocator;

    public CodexDiagnosticsService(IProcessRunner processRunner, IExecutableLocator executableLocator)
    {
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
        _executableLocator = executableLocator ?? throw new ArgumentNullException(nameof(executableLocator));
    }

    public async Task<CodexDiagnosticsResult> CheckAsync(string? configuredPath, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var executablePath = _executableLocator.Find(configuredPath);
        if (executablePath is null)
        {
            return new CodexDiagnosticsResult(
                null,
                IsInstalled: false,
                IsLoggedIn: false,
                "Codex CLI를 찾을 수 없습니다. 설치 후 다시 확인하거나 실행 파일 경로를 지정하세요.");
        }

        try
        {
            var result = await _processRunner.RunAsync(
                new ProcessRequest(
                    executablePath,
                    ["login", "status"],
                    AppContext.BaseDirectory,
                    StandardInput: null,
                    LoginStatusTimeout),
                cancellationToken).ConfigureAwait(false);

            if (result.TimedOut)
            {
                return new CodexDiagnosticsResult(
                    executablePath,
                    IsInstalled: true,
                    IsLoggedIn: false,
                    "Codex 로그인 상태 확인 시간이 초과되었습니다. 네트워크와 Codex CLI 상태를 확인한 후 다시 시도하세요.");
            }

            if (result.ExitCode == 0)
            {
                return new CodexDiagnosticsResult(
                    executablePath,
                    IsInstalled: true,
                    IsLoggedIn: true,
                    "Codex CLI가 설치되어 있고 로그인되어 있습니다.");
            }

            if (result.ExitCode == 1)
            {
                return new CodexDiagnosticsResult(
                    executablePath,
                    IsInstalled: true,
                    IsLoggedIn: false,
                    "Codex에 로그인되어 있지 않습니다. 터미널에서 'codex login'을 실행한 후 다시 확인하세요.");
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            // Process output can include credential data, so it is intentionally never surfaced here.
        }

        return new CodexDiagnosticsResult(
            executablePath,
            IsInstalled: true,
            IsLoggedIn: false,
            "Codex 로그인 상태를 확인하지 못했습니다. Codex CLI를 다시 로그인한 후 재시도하세요.");
    }
}

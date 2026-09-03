using Civ6Companion.App.Advisor;
using Civ6Companion.Tests.TestSupport;
using FluentAssertions;

namespace Civ6Companion.Tests.Advisor;

public sealed class CodexDiagnosticsServiceTests
{
    [Fact]
    public async Task CheckAsync_WhenLoginStatusSucceeds_ReturnsReady()
    {
        var runner = new StubProcessRunner(new ProcessResult(0, "Logged in using ChatGPT", "", false));
        var service = new CodexDiagnosticsService(runner, new StubExecutableLocator(@"C:\\codex.exe"));

        var result = await service.CheckAsync(null, CancellationToken.None);

        result.IsInstalled.Should().BeTrue();
        result.IsLoggedIn.Should().BeTrue();
        result.Message.Should().Be("Codex CLI가 설치되어 있고 로그인되어 있습니다.");
        runner.LastRequest!.FileName.Should().Be(@"C:\\codex.exe");
        runner.LastRequest.Arguments.Should().Equal("login", "status");
        runner.LastRequest.StandardInput.Should().BeNull();
    }

    [Fact]
    public async Task CheckAsync_WhenExecutableIsMissing_ReturnsKoreanInstallationGuidance()
    {
        var service = new CodexDiagnosticsService(
            new StubProcessRunner(new ProcessResult(0, "", "", false)),
            new StubExecutableLocator(null));

        var result = await service.CheckAsync(null, CancellationToken.None);

        result.IsInstalled.Should().BeFalse();
        result.IsLoggedIn.Should().BeFalse();
        result.Message.Should().Be("Codex CLI를 찾을 수 없습니다. 설치 후 다시 확인하거나 실행 파일 경로를 지정하세요.");
    }

    [Fact]
    public async Task CheckAsync_WhenLoginStatusFails_ReturnsKoreanLoginGuidance()
    {
        var runner = new StubProcessRunner(new ProcessResult(1, "", "not logged in", false));
        var service = new CodexDiagnosticsService(runner, new StubExecutableLocator(@"C:\\codex.exe"));

        var result = await service.CheckAsync(null, CancellationToken.None);

        result.IsInstalled.Should().BeTrue();
        result.IsLoggedIn.Should().BeFalse();
        result.Message.Should().Be("Codex에 로그인되어 있지 않습니다. 터미널에서 'codex login'을 실행한 후 다시 확인하세요.");
    }

    [Fact]
    public async Task CheckAsync_WhenLoginStatusTimesOut_ReturnsKoreanRetryGuidance()
    {
        var runner = new StubProcessRunner(new ProcessResult(-1, "", "", true));
        var service = new CodexDiagnosticsService(runner, new StubExecutableLocator(@"C:\\codex.exe"));

        var result = await service.CheckAsync(null, CancellationToken.None);

        result.IsInstalled.Should().BeTrue();
        result.IsLoggedIn.Should().BeFalse();
        result.Message.Should().Be("Codex 로그인 상태 확인 시간이 초과되었습니다. 네트워크와 Codex CLI 상태를 확인한 후 다시 시도하세요.");
    }

    [Fact]
    public async Task CheckAsync_WhenStderrContainsCredentials_DoesNotExposeThemInTheUserMessage()
    {
        const string stderr = "access_token=super-secret-token";
        var runner = new StubProcessRunner(new ProcessResult(2, "", stderr, false));
        var service = new CodexDiagnosticsService(runner, new StubExecutableLocator(@"C:\\codex.exe"));

        var result = await service.CheckAsync(null, CancellationToken.None);

        result.Message.Should().NotContain(stderr);
        result.Message.Should().NotContain("super-secret-token");
        result.Message.Should().Be("Codex 로그인 상태를 확인하지 못했습니다. Codex CLI를 다시 로그인한 후 재시도하세요.");
    }
}

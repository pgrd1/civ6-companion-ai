using Civ6Companion.App.Advisor;
using Civ6Companion.Tests.TestSupport;
using FluentAssertions;

namespace Civ6Companion.Tests.Advisor;

public sealed class CodexCliClientTests
{
    [Fact]
    public async Task AnalyzeAsync_AddsEveryImageInChronologicalOrder()
    {
        using var temp = new TempDirectory();
        var runner = new StubProcessRunner(request =>
        {
            var outputIndex = request.Arguments.ToList().IndexOf("--output-last-message");
            File.WriteAllText(request.Arguments[outputIndex + 1], ValidAnalysis);
            return new ProcessResult(0, "", "", false);
        });
        var client = new CodexCliClient(runner, new StubExecutableLocator("codex.exe"),
            Path.Combine(temp.Path, "analysis.json"), Path.Combine(temp.Path, "chat.json"), temp.Path);

        await client.AnalyzeAsync(["first.png", "second.png", "current.png"], "prompt", CancellationToken.None);

        ImageArguments(runner.LastRequest!.Arguments).Should().Equal("first.png", "second.png", "current.png");
    }

    [Fact]
    public async Task AnalyzeAsync_UsesFastIsolatedSolInvocation()
    {
        using var temp = new TempDirectory();
        var runner = new StubProcessRunner(request =>
        {
            var outputIndex = request.Arguments.ToList().IndexOf("--output-last-message");
            File.WriteAllText(request.Arguments[outputIndex + 1], ValidAnalysis);
            return new ProcessResult(0, "", "", false);
        });
        var client = new CodexCliClient(runner, new StubExecutableLocator("codex.exe"),
            Path.Combine(temp.Path, "analysis.json"), Path.Combine(temp.Path, "chat.json"), temp.Path);

        await client.AnalyzeAsync(["current.png"], "prompt", CancellationToken.None);

        runner.LastRequest!.Arguments.Should().ContainInOrder(
            "exec", "--ignore-user-config", "--ignore-rules", "--ephemeral",
            "--model", "gpt-5.6-sol", "--config", "model_reasoning_effort=\"low\"");
        runner.LastRequest.Timeout.Should().Be(TimeSpan.FromSeconds(25));
    }

    [Fact]
    public async Task AnalyzeAsync_DoesNotRetryAfterFastTimeout()
    {
        using var temp = new TempDirectory();
        var runner = new StubProcessRunner(new ProcessResult(-1, "", "", true));
        var client = new CodexCliClient(runner, new StubExecutableLocator("codex.exe"),
            Path.Combine(temp.Path, "analysis.json"), Path.Combine(temp.Path, "chat.json"), temp.Path);

        var action = () => client.AnalyzeAsync(["current.png"], "prompt", CancellationToken.None);

        await action.Should().ThrowAsync<CodexClientException>()
            .Where(exception => exception.Code == "CODEX_TIMEOUT");
        runner.Requests.Should().ContainSingle();
    }

    [Fact]
    public async Task AnalyzeAsync_FailedProcessUsesGenericErrorWithoutStderr()
    {
        using var temp = new TempDirectory();
        var client = new CodexCliClient(new StubProcessRunner(new ProcessResult(1, "", "C:\\private\\token prompt", false)),
            new StubExecutableLocator("codex.exe"), Path.Combine(temp.Path, "analysis.json"), Path.Combine(temp.Path, "chat.json"), temp.Path);

        var action = () => client.AnalyzeAsync(["current.png"], "prompt", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodexClientException>();
        exception.Which.Code.Should().Be("CODEX_FAILED");
        exception.Which.Message.Should().Be("Codex 실행에 실패했습니다.");
    }

    [Fact]
    public async Task AnalyzeAsync_UnauthorizedResultCleanupDoesNotMaskBadResponse()
    {
        using var temp = new TempDirectory();
        string? resultPath = null;
        var runner = new StubProcessRunner(request =>
        {
            var outputIndex = request.Arguments.ToList().IndexOf("--output-last-message");
            resultPath = request.Arguments[outputIndex + 1];
            Directory.CreateDirectory(resultPath);
            return new ProcessResult(0, "", "", false);
        });
        var client = new CodexCliClient(runner, new StubExecutableLocator("codex.exe"),
            Path.Combine(temp.Path, "analysis.json"), Path.Combine(temp.Path, "chat.json"), temp.Path);

        var action = () => client.AnalyzeAsync(["current.png"], "prompt", CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodexClientException>();
        exception.Which.Code.Should().Be("CODEX_BAD_RESPONSE");
        Directory.Delete(resultPath!);
    }

    private static IEnumerable<string> ImageArguments(IReadOnlyList<string> arguments)
    {
        for (var index = 0; index < arguments.Count - 1; index++)
            if (arguments[index] == "--image") yield return Path.GetFileName(arguments[index + 1]);
    }

    private const string ValidAnalysis = """
        {"screenType":"map","confidence":1,"title":"ok","immediateActions":[],"nextSteps":[],"warnings":[],"fiveTurnGoals":[],"needsAnotherScreen":null,"stateUpdate":"ok"}
        """;
}

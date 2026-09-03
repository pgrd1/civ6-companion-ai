using System.Diagnostics;
using System.Text;
using System.Text.Json;
using Civ6Companion.App.Advisor;
using Civ6Companion.Tests.TestSupport;
using FluentAssertions;

namespace Civ6Companion.Tests.Advisor;

public sealed class ProcessRunnerTests
{
    private const int OneMiB = 1024 * 1024;

    [Fact]
    public async Task RunAsync_WritesStandardInputAsUtf8WithoutBom()
    {
        const string prompt = "문명 6 다음 행동";
        var runner = new ProcessRunner();

        var result = await runner.RunAsync(
            CreateRequest("stdin-bytes", TimeSpan.FromSeconds(3), prompt),
            CancellationToken.None);

        result.ExitCode.Should().Be(0);
        result.StandardOutput.Should().Be(Convert.ToHexString(Encoding.UTF8.GetBytes(prompt)));
    }

    [Fact]
    public async Task RunAsync_WhenChildDoesNotReadLargeStdin_TimesOutAndKillsItsProcessTree()
    {
        using var temp = new TempDirectory();
        var readyPath = Path.Combine(temp.Path, "child-ready.txt");
        var survivedPath = Path.Combine(temp.Path, "child-survived.txt");
        using var callerCancellation = new CancellationTokenSource();
        var runner = new ProcessRunner();
        var request = CreateRequest(
            "blocked-stdin",
            TimeSpan.FromMilliseconds(250),
            new string('I', 8 * OneMiB),
            readyPath,
            survivedPath);
        var runTask = runner.RunAsync(request, callerCancellation.Token);

        try
        {
            await WaitForFileAsync(readyPath, TimeSpan.FromSeconds(2));
            var completed = await Task.WhenAny(runTask, Task.Delay(TimeSpan.FromSeconds(2)));

            completed.Should().BeSameAs(runTask, "the process timeout must govern a blocked stdin write");
            var result = await runTask;
            result.TimedOut.Should().BeTrue();
            await Task.Delay(TimeSpan.FromSeconds(3));
            File.Exists(survivedPath).Should().BeFalse("timeout cleanup must kill the child process too");
        }
        finally
        {
            callerCancellation.Cancel();
            await ObserveCompletionAsync(runTask);
        }
    }

    [Fact]
    public async Task RunAsync_WhenCallerCancels_ThrowsAndKillsItsProcessTree()
    {
        using var temp = new TempDirectory();
        var readyPath = Path.Combine(temp.Path, "child-ready.txt");
        var survivedPath = Path.Combine(temp.Path, "child-survived.txt");
        using var callerCancellation = new CancellationTokenSource();
        var runner = new ProcessRunner();
        var runTask = runner.RunAsync(
            CreateRequest("blocked-stdin", TimeSpan.FromSeconds(5), null, readyPath, survivedPath),
            callerCancellation.Token);

        await WaitForFileAsync(readyPath, TimeSpan.FromSeconds(2));
        callerCancellation.Cancel();

        var action = () => runTask;
        await action.Should().ThrowAsync<OperationCanceledException>();
        await Task.Delay(TimeSpan.FromSeconds(3));
        File.Exists(survivedPath).Should().BeFalse();
    }

    [Fact]
    public async Task RunAsync_PreservesArgumentsAndCapsBothOutputStreamsWithoutDeadlock()
    {
        var runner = new ProcessRunner();
        var argumentResult = await runner.RunAsync(
            CreateRequest("arguments", TimeSpan.FromSeconds(3), null, "space value", "&|<>^%", "\"quoted\""),
            CancellationToken.None);
        var receivedArguments = JsonSerializer.Deserialize<string[]>(argumentResult.StandardOutput);

        argumentResult.ExitCode.Should().Be(0);
        receivedArguments.Should().Equal("space value", "&|<>^%", "\"quoted\"");

        var outputResult = await runner.RunAsync(
            CreateRequest("output", TimeSpan.FromSeconds(3), null),
            CancellationToken.None);

        outputResult.ExitCode.Should().Be(0);
        outputResult.StandardOutput.Length.Should().Be(OneMiB);
        outputResult.StandardError.Length.Should().Be(OneMiB);
    }

    [Fact]
    public void FindDotnetPath_WhenProjectLocalSdkIsMissing_FindsExecutableOnPath()
    {
        using var temp = new TempDirectory();
        var expectedPath = Path.Combine(temp.Path, "dotnet.exe");
        File.WriteAllBytes(expectedPath, []);

        var result = FindDotnetPath(temp.Path, temp.Path);

        result.Should().Be(expectedPath);
    }

    private static ProcessRequest CreateRequest(string mode, TimeSpan timeout, string? standardInput, params string[] arguments) =>
        new(
            FindDotnetPath(AppContext.BaseDirectory, Environment.GetEnvironmentVariable("PATH")),
            [FindHelperAssemblyPath(), mode, .. arguments],
            AppContext.BaseDirectory,
            standardInput,
            timeout);

    private static string FindHelperAssemblyPath()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Civ6Companion.ProcessRunnerHelper.dll");
        File.Exists(path).Should().BeTrue("the project reference must copy the deterministic helper");
        return path;
    }

    private static string FindDotnetPath(string startDirectory, string? path)
    {
        for (var directory = new DirectoryInfo(startDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, ".dotnet", "dotnet.exe");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }

        foreach (var directory in (path ?? string.Empty).Split(
                     Path.PathSeparator,
                     StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            var candidate = Path.Combine(directory, "dotnet.exe");
            if (File.Exists(candidate))
            {
                return Path.GetFullPath(candidate);
            }
        }

        throw new DirectoryNotFoundException("A .NET host was not found in the project-local SDK or PATH.");
    }

    private static async Task WaitForFileAsync(string path, TimeSpan timeout)
    {
        var stopwatch = Stopwatch.StartNew();
        while (!File.Exists(path))
        {
            if (stopwatch.Elapsed >= timeout)
            {
                throw new TimeoutException($"The helper did not create '{Path.GetFileName(path)}'.");
            }

            await Task.Delay(20);
        }
    }

    private static async Task ObserveCompletionAsync(Task task)
    {
        try
        {
            await task.WaitAsync(TimeSpan.FromSeconds(3));
        }
        catch (OperationCanceledException)
        {
            // Caller cancellation is expected while cleaning up the RED test.
        }
    }
}

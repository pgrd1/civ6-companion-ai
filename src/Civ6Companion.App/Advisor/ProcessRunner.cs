using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Text;

namespace Civ6Companion.App.Advisor;

public sealed class ProcessRunner : IProcessRunner
{
    private const int MaximumCapturedStreamBytes = 1024 * 1024;

    public async Task<ProcessResult> RunAsync(ProcessRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        ValidateRequest(request);
        cancellationToken.ThrowIfCancellationRequested();

        using var process = new Process
        {
            StartInfo = CreateStartInfo(request),
        };

        if (!process.Start())
        {
            throw new InvalidOperationException("The process could not be started.");
        }

        using var timeout = new CancellationTokenSource(request.Timeout);
        using var operationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
        var operationToken = operationCancellation.Token;
        var standardOutputTask = ReadCappedAsync(process.StandardOutput.BaseStream, operationToken);
        var standardErrorTask = ReadCappedAsync(process.StandardError.BaseStream, operationToken);

        try
        {
            if (request.StandardInput is { } standardInput)
            {
                await process.StandardInput.WriteAsync(standardInput.AsMemory(), operationToken).ConfigureAwait(false);
            }

            process.StandardInput.Close();

            try
            {
                await process.WaitForExitAsync(operationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                await StopProcessAsync(process).ConfigureAwait(false);
                return await CreateTimedOutResultAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            }
            var capturedStreams = await CaptureStreamsAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            return new ProcessResult(process.ExitCode, capturedStreams.StandardOutput, capturedStreams.StandardError, TimedOut: false);
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
        {
            await StopProcessAsync(process).ConfigureAwait(false);
            return await CreateTimedOutResultAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            await StopProcessAsync(process).ConfigureAwait(false);
            await DrainCapturedStreamsAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await StopProcessAsync(process).ConfigureAwait(false);
            await DrainCapturedStreamsAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
            throw;
        }
    }

    private static ProcessStartInfo CreateStartInfo(ProcessRequest request)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = request.FileName,
            WorkingDirectory = request.WorkingDirectory,
            UseShellExecute = false,
            RedirectStandardInput = true,
            StandardInputEncoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false),
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };

        foreach (var argument in request.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        return startInfo;
    }

    private static void ValidateRequest(ProcessRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.FileName))
        {
            throw new ArgumentException("An executable file name is required.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.WorkingDirectory) || !Path.IsPathFullyQualified(request.WorkingDirectory))
        {
            throw new ArgumentException("An absolute working directory is required.", nameof(request));
        }

        if (request.Timeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(request), "A positive process timeout is required.");
        }

        if (request.Timeout > TimeSpan.FromMilliseconds(uint.MaxValue - 1))
        {
            throw new ArgumentOutOfRangeException(nameof(request), "The process timeout is too large.");
        }

        ArgumentNullException.ThrowIfNull(request.Arguments);
        if (request.Arguments.Any(static argument => argument is null))
        {
            throw new ArgumentException("Process arguments cannot contain null values.", nameof(request));
        }
    }

    private static async Task<(string StandardOutput, string StandardError)> CaptureStreamsAsync(
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        return (standardOutputTask.Result, standardErrorTask.Result);
    }

    private static async Task<ProcessResult> CreateTimedOutResultAsync(
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        var capturedStreams = await CaptureCancelledStreamsAsync(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        return new ProcessResult(-1, capturedStreams.StandardOutput, capturedStreams.StandardError, TimedOut: true);
    }

    private static async Task<(string StandardOutput, string StandardError)> CaptureCancelledStreamsAsync(
        Task<string> standardOutputTask,
        Task<string> standardErrorTask)
    {
        var standardOutput = await GetCapturedStreamAfterCancellationAsync(standardOutputTask).ConfigureAwait(false);
        var standardError = await GetCapturedStreamAfterCancellationAsync(standardErrorTask).ConfigureAwait(false);
        return (standardOutput, standardError);
    }

    private static async Task<string> GetCapturedStreamAfterCancellationAsync(Task<string> capturedStreamTask)
    {
        try
        {
            return await capturedStreamTask.ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            return string.Empty;
        }
        catch (IOException)
        {
            return string.Empty;
        }
        catch (ObjectDisposedException)
        {
            return string.Empty;
        }
    }

    private static async Task DrainCapturedStreamsAsync(Task<string> standardOutputTask, Task<string> standardErrorTask)
    {
        try
        {
            await Task.WhenAll(standardOutputTask, standardErrorTask).ConfigureAwait(false);
        }
        catch (IOException)
        {
            // The caller's cancellation or process-start failure is the actionable result.
        }
        catch (ObjectDisposedException)
        {
            // The caller's cancellation or process-start failure is the actionable result.
        }
        catch (OperationCanceledException)
        {
            // Cancellation interrupts stream reads after process-tree cleanup has been requested.
        }
    }

    private static async Task StopProcessAsync(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            return;
        }
        catch (System.ComponentModel.Win32Exception)
        {
            return;
        }

        try
        {
            await process.WaitForExitAsync().ConfigureAwait(false);
        }
        catch (InvalidOperationException)
        {
            // The process exited between Kill and WaitForExitAsync.
        }
    }

    private static async Task<string> ReadCappedAsync(Stream stream, CancellationToken cancellationToken)
    {
        var buffer = ArrayPool<byte>.Shared.Rent(81920);
        try
        {
            using var capturedBytes = new MemoryStream(MaximumCapturedStreamBytes);
            int bytesRead;

            while ((bytesRead = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false)) > 0)
            {
                var remainingCapacity = MaximumCapturedStreamBytes - (int)capturedBytes.Length;
                if (remainingCapacity > 0)
                {
                    var bytesToCapture = Math.Min(remainingCapacity, bytesRead);
                    await capturedBytes.WriteAsync(buffer.AsMemory(0, bytesToCapture)).ConfigureAwait(false);
                }
            }

            return Encoding.UTF8.GetString(capturedBytes.GetBuffer(), 0, (int)capturedBytes.Length);
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(buffer);
        }
    }
}

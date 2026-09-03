using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

return await RunAsync(args);

static async Task<int> RunAsync(string[] arguments)
{
    if (arguments.Length == 0)
    {
        return 64;
    }

    return arguments[0] switch
    {
        "arguments" => WriteArguments(arguments),
        "blocked-stdin" => await BlockStdinAsync(arguments).ConfigureAwait(false),
        "child-wait" => await WaitForChildAsync(arguments).ConfigureAwait(false),
        "output" => WriteLargeOutput(),
        "stdin-bytes" => await EchoStdinBytesAsync().ConfigureAwait(false),
        "wait" => await WaitAsync().ConfigureAwait(false),
        _ => 64,
    };
}

static async Task<int> EchoStdinBytesAsync()
{
    using var input = Console.OpenStandardInput();
    using var buffer = new MemoryStream();
    await input.CopyToAsync(buffer).ConfigureAwait(false);
    Console.Out.Write(Convert.ToHexString(buffer.ToArray()));
    return 0;
}

static int WriteArguments(string[] arguments)
{
    Console.Out.Write(JsonSerializer.Serialize(arguments[1..]));
    return 0;
}

static async Task<int> BlockStdinAsync(string[] arguments)
{
    if (arguments.Length != 3)
    {
        return 64;
    }

    var processPath = Environment.ProcessPath ?? throw new InvalidOperationException("The process path is unavailable.");
    var assemblyPath = Assembly.GetExecutingAssembly().Location;
    var childStart = new ProcessStartInfo
    {
        FileName = processPath,
        UseShellExecute = false,
        CreateNoWindow = true,
    };
    childStart.ArgumentList.Add(assemblyPath);
    childStart.ArgumentList.Add("child-wait");
    childStart.ArgumentList.Add(arguments[1]);
    childStart.ArgumentList.Add(arguments[2]);

    _ = Process.Start(childStart) ?? throw new InvalidOperationException("The child process could not be started.");
    await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    return 0;
}

static async Task<int> WaitForChildAsync(string[] arguments)
{
    if (arguments.Length != 3)
    {
        return 64;
    }

    await File.WriteAllTextAsync(arguments[1], "ready").ConfigureAwait(false);
    await Task.Delay(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
    await File.WriteAllTextAsync(arguments[2], "survived").ConfigureAwait(false);
    return 0;
}

static int WriteLargeOutput()
{
    var bytes = new byte[128 * 1024];
    Array.Fill(bytes, (byte)'X');

    using var standardOutput = Console.OpenStandardOutput();
    using var standardError = Console.OpenStandardError();
    for (var index = 0; index < 10; index++)
    {
        standardOutput.Write(bytes);
    }

    for (var index = 0; index < 10; index++)
    {
        standardError.Write(bytes);
    }

    return 0;
}

static async Task<int> WaitAsync()
{
    await Task.Delay(TimeSpan.FromSeconds(10)).ConfigureAwait(false);
    return 0;
}

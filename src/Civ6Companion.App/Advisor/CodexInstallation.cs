using System.IO;

namespace Civ6Companion.App.Advisor;

public sealed class CodexInstallation : IExecutableLocator
{
    private static readonly string[] ExecutableNames = ["codex.exe", "codex.cmd"];
    private readonly Func<string, string?> _environmentVariableReader;
    private readonly Func<Environment.SpecialFolder, string> _folderPathReader;

    public CodexInstallation()
        : this(Environment.GetEnvironmentVariable, Environment.GetFolderPath)
    {
    }

    public CodexInstallation(
        Func<string, string?> environmentVariableReader,
        Func<Environment.SpecialFolder, string> folderPathReader)
    {
        _environmentVariableReader = environmentVariableReader ?? throw new ArgumentNullException(nameof(environmentVariableReader));
        _folderPathReader = folderPathReader ?? throw new ArgumentNullException(nameof(folderPathReader));
    }

    public string? Find(string? configuredPath)
    {
        var configuredExecutable = FindConfiguredExecutable(configuredPath);
        if (configuredExecutable is not null)
        {
            return configuredExecutable;
        }

        var appData = _folderPathReader(Environment.SpecialFolder.ApplicationData);
        var npmDirectory = Path.Combine(appData, "npm");
        var npmNativeExecutable = FindNpmNativeExecutable(npmDirectory);
        if (npmNativeExecutable is not null)
        {
            return npmNativeExecutable;
        }

        var pathExecutable = FindOnPath(_environmentVariableReader("PATH"));
        if (pathExecutable is not null)
        {
            return pathExecutable;
        }

        return FindExecutable(Path.Combine(npmDirectory, "codex.exe"))
            ?? FindExecutable(Path.Combine(npmDirectory, "codex.cmd"));
    }

    private static string? FindConfiguredExecutable(string? configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
        {
            return null;
        }

        try
        {
            return Path.IsPathFullyQualified(configuredPath)
                ? FindExecutable(configuredPath)
                : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
    }

    private static string? FindOnPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        foreach (var directory in path.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Path.IsPathFullyQualified(directory))
            {
                continue;
            }

            var npmNativeExecutable = FindNpmNativeExecutable(directory);
            if (npmNativeExecutable is not null)
            {
                return npmNativeExecutable;
            }

            foreach (var executableName in ExecutableNames)
            {
                var executable = FindExecutable(Path.Combine(directory, executableName));
                if (executable is not null)
                {
                    return executable;
                }
            }
        }

        return null;
    }

    private static string? FindNpmNativeExecutable(string npmDirectory) => FindExecutable(Path.Combine(
        npmDirectory,
        "node_modules", "@openai", "codex", "node_modules", "@openai", "codex-win32-x64",
        "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe"));

    private static string? FindExecutable(string candidatePath)
    {
        try
        {
            return File.Exists(candidatePath) ? Path.GetFullPath(candidatePath) : null;
        }
        catch (ArgumentException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
        catch (NotSupportedException)
        {
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
    }
}

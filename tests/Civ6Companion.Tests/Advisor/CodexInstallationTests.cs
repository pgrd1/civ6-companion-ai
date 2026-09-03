using Civ6Companion.App.Advisor;
using Civ6Companion.Tests.TestSupport;
using FluentAssertions;

namespace Civ6Companion.Tests.Advisor;

public sealed class CodexInstallationTests
{
    [Fact]
    public void Find_WhenConfiguredExecutableExists_PrefersItOverPath()
    {
        using var temp = new TempDirectory();
        var configured = CreateFile(temp.Path, "configured", "codex.exe");
        _ = CreateFile(temp.Path, "path", "codex.exe");
        var locator = CreateLocator(Path.Combine(temp.Path, "path"), Path.Combine(temp.Path, "appdata"));

        var result = locator.Find(configured);

        result.Should().Be(Path.GetFullPath(configured));
    }

    [Fact]
    public void Find_WhenPathContainsExecutables_UsesDirectoryOrderAndPrefersExeToCmd()
    {
        using var temp = new TempDirectory();
        var firstDirectory = Path.Combine(temp.Path, "first");
        var secondDirectory = Path.Combine(temp.Path, "second");
        var preferred = CreateFile(firstDirectory, "codex.exe");
        _ = CreateFile(firstDirectory, "codex.cmd");
        _ = CreateFile(secondDirectory, "codex.exe");
        var locator = CreateLocator(string.Join(Path.PathSeparator, firstDirectory, secondDirectory), Path.Combine(temp.Path, "appdata"));

        var result = locator.Find(null);

        result.Should().Be(Path.GetFullPath(preferred));
    }

    [Fact]
    public void Find_WhenPathDirectoriesContainDifferentLaunchers_UsesTheFirstDirectory()
    {
        using var temp = new TempDirectory();
        var firstDirectory = Path.Combine(temp.Path, "first");
        var secondDirectory = Path.Combine(temp.Path, "second");
        var first = CreateFile(firstDirectory, "codex.cmd");
        _ = CreateFile(secondDirectory, "codex.exe");
        var locator = CreateLocator(string.Join(Path.PathSeparator, firstDirectory, secondDirectory), Path.Combine(temp.Path, "appdata"));

        var result = locator.Find(null);

        result.Should().Be(Path.GetFullPath(first));
    }

    [Fact]
    public void Find_WhenConfiguredPathIsRelative_UsesAppDataFallbackAndDoesNotProbeAuthFiles()
    {
        using var temp = new TempDirectory();
        var appData = Path.Combine(temp.Path, "appdata");
        var fallback = CreateFile(appData, "npm", "codex.cmd");
        _ = CreateFile(appData, ".codex", "auth.json");
        var locator = CreateLocator(Path.Combine(temp.Path, "missing"), appData);

        var result = locator.Find(Path.Combine("relative", "codex.exe"));

        result.Should().Be(Path.GetFullPath(fallback));
    }

    [Fact]
    public void Find_WhenNoExecutableExists_ReturnsMissingEvenWhenAnAuthFileExists()
    {
        using var temp = new TempDirectory();
        var appData = Path.Combine(temp.Path, "appdata");
        _ = CreateFile(appData, ".codex", "auth.json");
        var locator = CreateLocator(Path.Combine(temp.Path, "missing"), appData);

        var result = locator.Find(null);

        result.Should().BeNull();
    }

    [Fact]
    public void Find_WhenNpmLauncherAndNativeBinaryExist_PrefersNativeBinary()
    {
        using var temp = new TempDirectory();
        var appData = Path.Combine(temp.Path, "appdata");
        _ = CreateFile(appData, "npm", "codex.cmd");
        var native = CreateFile(appData, "npm", "node_modules", "@openai", "codex", "node_modules",
            "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
        var locator = CreateLocator(string.Empty, appData);

        var result = locator.Find(null);

        result.Should().Be(Path.GetFullPath(native));
    }

    [Fact]
    public void Find_WhenPathAndNpmNativeExist_PrefersNpmNativeWithUserLogin()
    {
        using var temp = new TempDirectory();
        var appData = Path.Combine(temp.Path, "appdata");
        var pathExecutable = CreateFile(temp.Path, "windows-apps", "codex.exe");
        var native = CreateFile(appData, "npm", "node_modules", "@openai", "codex", "node_modules",
            "@openai", "codex-win32-x64", "vendor", "x86_64-pc-windows-msvc", "bin", "codex.exe");
        var locator = CreateLocator(Path.GetDirectoryName(pathExecutable)!, appData);

        var result = locator.Find(null);

        result.Should().Be(Path.GetFullPath(native));
    }

    private static CodexInstallation CreateLocator(string path, string appData) =>
        new(
            name => name == "PATH" ? path : null,
            folder => folder == Environment.SpecialFolder.ApplicationData ? appData : string.Empty);

    private static string CreateFile(string root, params string[] segments)
    {
        var path = Path.Combine([root, .. segments]);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllText(path, string.Empty);
        return path;
    }
}

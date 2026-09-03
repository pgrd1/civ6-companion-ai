using FluentAssertions;

namespace Civ6Companion.Tests.Capture;

public sealed class NativeBuildPortabilityContractTests
{
    [Fact]
    public async Task NativeCmdWrapper_RejectsAQuotedMetacharacterConfigurationWithoutExecutingIt()
    {
        var root = FindRepositoryRoot();
        var wrapper = Path.Combine(root, "src", "Civ6Companion.WgcNative", "build-native.cmd");
        var marker = Path.Combine(Path.GetTempPath(), $"civ6-native-cmd-{Guid.NewGuid():N}.marker");
        var hostileConfiguration = $"Rejected\" & echo compromised > \"{marker}\" & rem \"";
        var startInfo = new System.Diagnostics.ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? @"C:\Windows\System32\cmd.exe",
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
        };
        startInfo.ArgumentList.Add("/d");
        startInfo.ArgumentList.Add("/s");
        startInfo.ArgumentList.Add("/c");
        startInfo.ArgumentList.Add("call");
        startInfo.ArgumentList.Add(wrapper);
        startInfo.ArgumentList.Add(hostileConfiguration);

        try
        {
            using var process = System.Diagnostics.Process.Start(startInfo);
            process.Should().NotBeNull();
            await process!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(10));
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();

            File.Exists(marker).Should().BeFalse();
            (output + error).Should().Contain("ValidateSet");
        }
        finally
        {
            File.Delete(marker);
        }
    }

    [Fact]
    public void NativeBuildContract_UsesValidatedDiscoveryAndBoundedCaptureWait()
    {
        var root = FindRepositoryRoot();
        var buildScript = File.ReadAllText(Path.Combine(root, "src", "Civ6Companion.WgcNative", "build-native.ps1"));
        var commandWrapper = File.ReadAllText(Path.Combine(root, "src", "Civ6Companion.WgcNative", "build-native.cmd"));
        var nativeProject = File.ReadAllText(Path.Combine(root, "src", "Civ6Companion.WgcNative", "Civ6Companion.WgcNative.vcxproj"));
        var captureImplementation = File.ReadAllText(Path.Combine(root, "src", "Civ6Companion.WgcNative", "WgcCapture.cpp"));
        var appProject = File.ReadAllText(Path.Combine(root, "src", "Civ6Companion.App", "Civ6Companion.App.csproj"));

        buildScript.Should().Contain("ValidateSet('Debug', 'Release')");
        buildScript.Should().Contain("ValidateScript");
        buildScript.Should().Contain("vswhere.exe");
        buildScript.Should().Contain("Microsoft.Component.MSBuild");
        buildScript.Should().Contain("MSBuildPath");
        buildScript.Should().Contain("GetEnvironmentVariable('Path', 'Process')");
        buildScript.Should().NotContain("Visual Studio\\18\\Community");
        buildScript.Should().NotContain("GetEnvironmentVariable('Path', 'Machine')");
        buildScript.Should().NotContain("GetEnvironmentVariable('Path', 'User')");
        commandWrapper.Should().Contain("build-native.ps1");
        commandWrapper.Should().NotContain("MSBuild.exe");
        nativeProject.Should().NotContain("<PlatformToolset>v145</PlatformToolset>");
        nativeProject.Should().NotContain("<WindowsTargetPlatformVersion>10.0.26100.0</WindowsTargetPlatformVersion>");
        nativeProject.Should().Contain("<PlatformToolset>$(DefaultPlatformToolset)</PlatformToolset>");
        captureImplementation.Should().Contain("75'000");
        captureImplementation.Should().Contain("HRESULT_FROM_WIN32(ERROR_TIMEOUT)");
        captureImplementation.Should().NotContain("WaitForMultipleObjects(2, handles, FALSE, INFINITE)");
        appProject.Should().Contain("WgcNativeBuildConfiguration");
        appProject.Should().Contain("Unsupported Configuration");
        appProject.Should().Contain("'$(Configuration)' != 'Debug' and '$(Configuration)' != 'Release'");
        appProject.Should().Contain("-Configuration &quot;$(WgcNativeBuildConfiguration)&quot;");

        var validationIndex = appProject.IndexOf("<Error Condition=\"'$(Configuration)' != 'Debug' and '$(Configuration)' != 'Release'\"", StringComparison.Ordinal);
        var execIndex = appProject.IndexOf("<Exec ", StringComparison.Ordinal);
        var copyIndex = appProject.IndexOf("<Copy ", StringComparison.Ordinal);
        var exec = appProject.Substring(execIndex, appProject.IndexOf(" />", execIndex, StringComparison.Ordinal) - execIndex);

        validationIndex.Should().BeGreaterThanOrEqualTo(0);
        validationIndex.Should().BeLessThan(execIndex);
        execIndex.Should().BeLessThan(copyIndex);
        exec.Should().NotContain("$(Configuration)");
    }

    private static string FindRepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Civ6CodexCompanion.sln")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the repository root.");
    }
}

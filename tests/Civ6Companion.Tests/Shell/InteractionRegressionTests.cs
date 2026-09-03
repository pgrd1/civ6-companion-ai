using System.Reflection;
using Civ6Companion.App.Advisor;
using Civ6Companion.App.Settings;
using FluentAssertions;

namespace Civ6Companion.Tests.Shell;

public sealed class InteractionRegressionTests
{
    [Fact]
    public void ChatInput_ActivatesOverlayWhenClickedWithoutStealingCaptureFocusOnShow()
    {
        var xamlPath = FindRepositoryFile("src", "Civ6Companion.App", "Shell", "OverlayWindow.xaml");
        var xaml = File.ReadAllText(xamlPath);

        xaml.Should().Contain("ShowActivated=\"False\"");
        xaml.Should().Contain("PreviewMouseLeftButtonDown=\"ChatBox_OnPreviewMouseLeftButtonDown\"");
    }

    [Fact]
    public void NewSession_DoesNotAssumeJapan()
    {
        var field = typeof(AdvisorOrchestrator).GetField(
            "DefaultCivilization",
            BindingFlags.NonPublic | BindingFlags.Static);

        field.Should().NotBeNull();
        field!.GetRawConstantValue().Should().Be("화면에서 자동 인식");
    }

    [Fact]
    public void Overlay_HasStableComposerAndExplicitSendButton()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "Civ6Companion.App", "Shell", "OverlayWindow.xaml"));

        xaml.Should().Contain("ResizeMode=\"NoResize\"");
        xaml.Should().Contain("Height=\"760\"");
        xaml.Should().Contain("PreviewKeyDown=\"ChatBox_OnPreviewKeyDown\"");
        xaml.Should().Contain("Content=\"보내기\"");
        xaml.Should().Contain("Command=\"{Binding SendChatCommand}\"");
        xaml.Should().NotContain("MaxHeight=\"110\"");
    }

    [Fact]
    public void Overlay_UsesTheSameWidthAsThePersistedDefault()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "Civ6Companion.App", "Shell", "OverlayWindow.xaml"));

        new AppSettings().OverlayWidth.Should().Be(460);
        xaml.Should().Contain("Width=\"460\"");
    }

    [Fact]
    public void Overlay_OffersExitInsteadOfTheDeadSettingsControl()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "Civ6Companion.App", "Shell", "OverlayWindow.xaml"));
        var windowCode = File.ReadAllText(FindRepositoryFile("src", "Civ6Companion.App", "Shell", "OverlayWindow.xaml.cs"));

        xaml.Should().Contain("Content=\"종료\"");
        xaml.Should().Contain("Command=\"{Binding ExitCommand}\"");
        xaml.Should().NotContain("SettingsCommand");
        windowCode.Should().Contain("Application.Current?.Shutdown()");
    }

    [Fact]
    public void Overlay_SeparatesNormalExitFromTheHideOnlyCloseAffordance()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "Civ6Companion.App", "Shell", "OverlayWindow.xaml"));
        var windowCode = File.ReadAllText(FindRepositoryFile("src", "Civ6Companion.App", "Shell", "OverlayWindow.xaml.cs"));

        xaml.Should().Contain("Content=\"×\" Command=\"{Binding HideCommand}\"");
        windowCode.Should().Contain("if (!_exitRequested)");
        windowCode.Should().Contain("eventArgs.Cancel = true;");
        windowCode.Should().Contain("Hide();");
        windowCode.Should().Contain("Close();");
    }

    [Fact]
    public void Overlay_KeepsTheConfirmationBeforeStartingNewGameAsynchronously()
    {
        var xaml = File.ReadAllText(FindRepositoryFile("src", "Civ6Companion.App", "Shell", "OverlayWindow.xaml"));
        var windowCode = File.ReadAllText(FindRepositoryFile("src", "Civ6Companion.App", "Shell", "OverlayWindow.xaml.cs"));

        xaml.Should().Contain("Command=\"{Binding RequestNewGameCommand}\"");
        windowCode.Should().Contain("MessageBox.Show");
        windowCode.Should().Contain("_viewModel.NewGameCommand.Execute(null)");
    }

    [Fact]
    public void Application_RegistersF7CaptureAndF8AnalysisSeparately()
    {
        var source = File.ReadAllText(FindRepositoryFile("src", "Civ6Companion.App", "App.xaml.cs"));

        source.Should().Contain("HotkeyGesture.TryParse(\"F7\"");
        source.Should().Contain("QueueCaptureCommand");
        source.Should().Contain("AnalyzeCommand");
    }

    private static string FindRepositoryFile(params string[] parts)
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine([directory.FullName, .. parts]);
            if (File.Exists(candidate)) return candidate;
        }

        throw new FileNotFoundException("Repository file was not found.", Path.Combine(parts));
    }
}

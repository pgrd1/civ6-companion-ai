using System.IO;
using System.Windows;
using Civ6Companion.App.Advisor;
using Civ6Companion.App.Capture;
using Civ6Companion.App.Common;
using Civ6Companion.App.Hotkeys;
using Civ6Companion.App.Settings;
using Civ6Companion.App.Shell;
using Civ6Companion.App.State;

namespace Civ6Companion.App;

public partial class App : Application
{
    private Win32HotkeyService? _analysisHotkey;
    private Win32HotkeyService? _captureHotkey;
    private AdvisorOrchestrator? _advisor;
    private OverlayViewModel? _viewModel;
    private OverlayWindow? _overlay;

    private async void OnStartup(object sender, StartupEventArgs eventArgs)
    {
        try
        {
            var localRoot = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "Civ6CodexCompanion");
            var settings = await new JsonSettingsStore(Path.Combine(localRoot, "Settings"))
                .LoadAsync(CancellationToken.None);
            var clock = new SystemClock();
            var store = new JsonConversationStore(Path.Combine(localRoot, "State"), clock);
            var capture = new CompositeCaptureService(
                new WindowsGraphicsCaptureBackend(),
                new GdiScreenCaptureBackend(),
                Path.Combine(localRoot, "Captures"),
                settings.KeepScreenshots);
            var schemaRoot = Path.Combine(AppContext.BaseDirectory, "Advisor");
            var client = new CodexCliClient(
                new ProcessRunner(),
                new CodexInstallation(),
                Path.Combine(schemaRoot, "analysis-response.schema.json"),
                Path.Combine(schemaRoot, "chat-response.schema.json"),
                Path.Combine(localRoot, "CodexWork"),
                settings.CodexPath);

            _advisor = new AdvisorOrchestrator(
                new CivWindowLocator(new Win32WindowApi()), capture, client, new PromptBuilder(), store, clock);
            _viewModel = new OverlayViewModel(_advisor);
            _overlay = new OverlayWindow(_viewModel)
            {
                Left = settings.OverlayLeft,
                Top = settings.OverlayTop,
                Width = settings.OverlayWidth,
            };
            _overlay.Show();

            if (!HotkeyGesture.TryParse(settings.Hotkey, out var gesture, out var parseError))
                throw new InvalidOperationException(parseError);

            _analysisHotkey = new Win32HotkeyService();
            _analysisHotkey.Pressed += OnAnalysisHotkeyPressed;
            var registration = _analysisHotkey.Register(gesture);
            if (!registration.IsSuccess)
                MessageBox.Show(registration.ErrorMessage, "문명 6 Codex 도우미", MessageBoxButton.OK, MessageBoxImage.Warning);

            if (!HotkeyGesture.TryParse("F7", out var captureGesture, out var captureParseError))
                throw new InvalidOperationException(captureParseError);

            _captureHotkey = new Win32HotkeyService();
            _captureHotkey.Pressed += OnCaptureHotkeyPressed;
            var captureRegistration = _captureHotkey.Register(captureGesture);
            if (!captureRegistration.IsSuccess)
                MessageBox.Show(captureRegistration.ErrorMessage, "문명 6 Codex 도우미", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
        catch (Exception exception)
        {
            MessageBox.Show($"도우미를 시작하지 못했습니다.\n{exception.Message}", "문명 6 Codex 도우미",
                MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(1);
        }
    }

    private void OnAnalysisHotkeyPressed(object? sender, EventArgs eventArgs)
    {
        if (_overlay is null || _viewModel is null) return;
        _overlay.Show();
        _viewModel.AnalyzeCommand.Execute(null);
    }

    private void OnCaptureHotkeyPressed(object? sender, EventArgs eventArgs)
    {
        if (_overlay is null || _viewModel is null) return;
        _overlay.Show();
        _viewModel.QueueCaptureCommand.Execute(null);
    }

    private void OnExit(object sender, ExitEventArgs eventArgs)
    {
        if (_analysisHotkey is not null) _analysisHotkey.Pressed -= OnAnalysisHotkeyPressed;
        if (_captureHotkey is not null) _captureHotkey.Pressed -= OnCaptureHotkeyPressed;
        _analysisHotkey?.Dispose();
        _captureHotkey?.Dispose();
        _viewModel?.Dispose();
        _advisor?.Dispose();
    }
}

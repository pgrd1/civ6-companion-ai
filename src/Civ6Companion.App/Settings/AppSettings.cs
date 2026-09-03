namespace Civ6Companion.App.Settings;

public sealed record AppSettings(
    string Hotkey = "F8",
    double OverlayLeft = 120,
    double OverlayTop = 80,
    double OverlayWidth = 460,
    bool KeepScreenshots = false,
    string? CodexPath = null);

namespace Civ6Companion.App.Hotkeys;

public sealed record HotkeyRegistrationResult(bool IsSuccess, string? ErrorMessage = null)
{
    public static HotkeyRegistrationResult Success { get; } = new(true);
}

public interface IHotkeyService : IDisposable
{
    event EventHandler? Pressed;
    HotkeyRegistrationResult Register(HotkeyGesture gesture);
    void Unregister();
}

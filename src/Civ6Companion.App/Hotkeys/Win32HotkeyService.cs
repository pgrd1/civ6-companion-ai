using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Civ6Companion.App.Hotkeys;

public sealed class Win32HotkeyService : IHotkeyService
{
    private const int HotkeyId = 0xC166;
    private const int WmHotkey = 0x0312;
    private static readonly nint MessageOnlyWindow = new(-3);
    private readonly HwndSource _source;
    private bool _registered;
    private bool _disposed;

    public Win32HotkeyService()
    {
        var parameters = new HwndSourceParameters("Civ6CodexCompanion.Hotkey")
        {
            ParentWindow = MessageOnlyWindow,
            Width = 0,
            Height = 0,
            WindowStyle = 0,
        };
        _source = new HwndSource(parameters);
        _source.AddHook(WindowProcedure);
    }

    public event EventHandler? Pressed;

    public HotkeyRegistrationResult Register(HotkeyGesture gesture)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Unregister();
        if (!NativeMethods.RegisterHotKey(_source.Handle, HotkeyId, (uint)gesture.Modifiers, gesture.VirtualKey))
        {
            var error = new Win32Exception(Marshal.GetLastWin32Error());
            return new(false, $"단축키 {gesture}을(를) 등록하지 못했습니다. 설정에서 다른 키를 선택하세요. ({error.Message})");
        }

        _registered = true;
        return HotkeyRegistrationResult.Success;
    }

    public void Unregister()
    {
        if (!_registered) return;
        NativeMethods.UnregisterHotKey(_source.Handle, HotkeyId);
        _registered = false;
    }

    public void Dispose()
    {
        if (_disposed) return;
        Unregister();
        _source.RemoveHook(WindowProcedure);
        _source.Dispose();
        _disposed = true;
    }

    private nint WindowProcedure(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        if (message == WmHotkey && wParam == HotkeyId)
        {
            handled = true;
            Pressed?.Invoke(this, EventArgs.Empty);
        }

        return 0;
    }

    private static partial class NativeMethods
    {
        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool RegisterHotKey(nint window, int id, uint modifiers, uint virtualKey);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        internal static extern bool UnregisterHotKey(nint window, int id);
    }
}

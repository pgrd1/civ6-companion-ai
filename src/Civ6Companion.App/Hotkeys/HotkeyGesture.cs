namespace Civ6Companion.App.Hotkeys;

[Flags]
public enum HotkeyModifiers : uint
{
    None = 0,
    Alt = 0x0001,
    Control = 0x0002,
    Shift = 0x0004,
    Windows = 0x0008,
}

public readonly record struct HotkeyGesture(HotkeyModifiers Modifiers, uint VirtualKey, string KeyName)
{
    public static bool TryParse(string? text, out HotkeyGesture gesture, out string? error)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text))
        {
            error = "단축키를 입력하세요.";
            return false;
        }

        var modifiers = HotkeyModifiers.None;
        string? keyName = null;
        uint virtualKey = 0;
        foreach (var rawPart in text.Split('+', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries))
        {
            if (TryGetModifier(rawPart, out var modifier))
            {
                if ((modifiers & modifier) != 0)
                {
                    error = $"중복된 보조키입니다: {rawPart}";
                    return false;
                }

                modifiers |= modifier;
                continue;
            }

            if (keyName is not null || !TryGetVirtualKey(rawPart, out virtualKey, out keyName))
            {
                error = $"지원하지 않는 키입니다: {rawPart}";
                return false;
            }
        }

        if (keyName is null)
        {
            error = "일반 키가 하나 필요합니다.";
            return false;
        }

        gesture = new(modifiers, virtualKey, keyName);
        error = null;
        return true;
    }

    public override string ToString()
    {
        var parts = new List<string>(5);
        if (Modifiers.HasFlag(HotkeyModifiers.Control)) parts.Add("Ctrl");
        if (Modifiers.HasFlag(HotkeyModifiers.Shift)) parts.Add("Shift");
        if (Modifiers.HasFlag(HotkeyModifiers.Alt)) parts.Add("Alt");
        if (Modifiers.HasFlag(HotkeyModifiers.Windows)) parts.Add("Win");
        parts.Add(KeyName);
        return string.Join('+', parts);
    }

    private static bool TryGetModifier(string value, out HotkeyModifiers modifier)
    {
        modifier = value.ToUpperInvariant() switch
        {
            "CTRL" or "CONTROL" => HotkeyModifiers.Control,
            "SHIFT" => HotkeyModifiers.Shift,
            "ALT" => HotkeyModifiers.Alt,
            "WIN" or "WINDOWS" => HotkeyModifiers.Windows,
            _ => HotkeyModifiers.None,
        };
        return modifier != HotkeyModifiers.None;
    }

    private static bool TryGetVirtualKey(string value, out uint virtualKey, out string? canonical)
    {
        var upper = value.ToUpperInvariant();
        if (upper.Length >= 2 && upper[0] == 'F' && int.TryParse(upper.AsSpan(1), out var function) && function is >= 1 and <= 24)
        {
            virtualKey = (uint)(0x70 + function - 1);
            canonical = $"F{function}";
            return true;
        }

        if (upper.Length == 1 && char.IsAsciiLetterOrDigit(upper[0]))
        {
            virtualKey = upper[0];
            canonical = upper;
            return true;
        }

        virtualKey = 0;
        canonical = null;
        return false;
    }
}

using Civ6Companion.App.Hotkeys;
using FluentAssertions;

namespace Civ6Companion.Tests.Hotkeys;

public sealed class HotkeyGestureTests
{
    [Theory]
    [InlineData("F8", HotkeyModifiers.None, 0x77, "F8")]
    [InlineData("Ctrl+Shift+F8", HotkeyModifiers.Control | HotkeyModifiers.Shift, 0x77, "Ctrl+Shift+F8")]
    [InlineData(" alt + f4 ", HotkeyModifiers.Alt, 0x73, "Alt+F4")]
    public void Parse_ValidGesture_ReturnsCanonicalGesture(
        string text, HotkeyModifiers modifiers, uint virtualKey, string canonical)
    {
        var result = HotkeyGesture.TryParse(text, out var gesture, out var error);

        result.Should().BeTrue();
        error.Should().BeNull();
        gesture.Modifiers.Should().Be(modifiers);
        gesture.VirtualKey.Should().Be(virtualKey);
        gesture.ToString().Should().Be(canonical);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl+Ctrl+F8")]
    [InlineData("Shift")]
    [InlineData("F25")]
    [InlineData("Ctrl+NoSuchKey")]
    public void Parse_InvalidGesture_ReturnsUsefulError(string text)
    {
        HotkeyGesture.TryParse(text, out _, out var error).Should().BeFalse();
        error.Should().NotBeNullOrWhiteSpace();
    }
}

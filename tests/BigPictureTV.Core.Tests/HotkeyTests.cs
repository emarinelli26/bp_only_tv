using BigPictureTV.Core.Input;

namespace BigPictureTV.Core.Tests;

public class HotkeyTests
{
    [Fact]
    public void DefaultsReadAsExpected()
    {
        Assert.Equal("Ctrl+Alt+F12", Hotkey.DefaultToggle.ToString());
        Assert.Equal("Ctrl+Alt+Shift+F12", Hotkey.Emergency.ToString());
        Assert.Equal("Ctrl+Alt+F12", new AppSettings().Hotkey);
    }

    [Theory]
    [InlineData("Ctrl+Alt+F12", "Ctrl+Alt+F12")]
    [InlineData(" alt + ctrl + f12 ", "Ctrl+Alt+F12")]
    [InlineData("Win+Shift+B", "Shift+Win+B")]
    [InlineData("Control+Num5", "Ctrl+Num5")]
    [InlineData("F13", "F13")]
    [InlineData("Ctrl+0xBA", "Ctrl+0xBA")]
    public void ParsesAndFormats(string text, string expected) =>
        Assert.Equal(expected, Hotkey.Parse(text)?.ToString());

    [Theory]
    [InlineData("")]
    [InlineData("F12")]          // no modifier
    [InlineData("Ctrl+Alt")]     // no key
    [InlineData("Ctrl+A+B")]     // two keys
    [InlineData("Ctrl+Banana")]
    public void RejectsWhatCantBeAHotkey(string text) => Assert.Null(Hotkey.Parse(text));

    [Fact]
    public void ARoundTripKeepsTheCombination()
    {
        var k = new Hotkey(KeyModifiers.Ctrl | KeyModifiers.Shift, 0x24);
        Assert.Equal(k, Hotkey.Parse(k.ToString()));
    }

    [Fact]
    public void AModifierKeyAloneIsNotValid() =>
        Assert.False(new Hotkey(KeyModifiers.Ctrl, 0xA2).IsValid);
}

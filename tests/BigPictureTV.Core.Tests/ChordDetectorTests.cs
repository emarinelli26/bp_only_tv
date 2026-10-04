using BigPictureTV.Core.Input;

namespace BigPictureTV.Core.Tests;

public class ChordDetectorTests
{
    const uint LCtrl = 0xA2, RCtrl = 0xA3, LAlt = 0xA4, LShift = 0xA0, F12 = 0x7B;
    readonly ChordDetector _d = new() { Hotkeys = new[] { Hotkey.DefaultToggle, Hotkey.Emergency } };

    [Fact]
    public void ModifiersFirst()
    {
        Assert.Null(_d.KeyDown(LCtrl));
        Assert.Null(_d.KeyDown(LAlt));
        Assert.Equal(Hotkey.DefaultToggle, _d.KeyDown(F12));
    }

    [Fact]
    public void KeyFirstWorksToo()
    {
        Assert.Null(_d.KeyDown(F12));
        Assert.Null(_d.KeyDown(LAlt));
        Assert.Equal(Hotkey.DefaultToggle, _d.KeyDown(RCtrl));
    }

    [Fact]
    public void FiresOncePerPress()
    {
        _d.KeyDown(LCtrl); _d.KeyDown(LAlt);
        Assert.NotNull(_d.KeyDown(F12));
        Assert.Null(_d.KeyDown(F12)); // auto-repeat
        _d.KeyUp(F12);
        Assert.NotNull(_d.KeyDown(F12)); // pressed again while holding Ctrl+Alt
    }

    [Fact]
    public void NeedsTheExactModifiers()
    {
        _d.KeyDown(LCtrl); _d.KeyDown(LAlt); _d.KeyDown(LShift);
        Assert.Equal(Hotkey.Emergency, _d.KeyDown(F12));
    }

    [Fact]
    public void IgnoresOtherCombinations()
    {
        _d.KeyDown(LCtrl);
        Assert.Null(_d.KeyDown(F12));
    }

    [Fact]
    public void ForgetsKeysReleasedOutOfSight()
    {
        _d.KeyDown(LCtrl); // its release is never seen
        _d.KeyDown(LAlt);
        Assert.Null(_d.KeyDown(F12, isDown: k => k != LCtrl));
    }
}

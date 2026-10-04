using BigPictureTV.Core.Input;

namespace BigPictureTV.Core.Tests;

public class GamepadComboTests
{
    static readonly DateTime T0 = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
    const GamepadButtons Combo = GamepadCombo.Default;

    [Fact]
    public void DefaultReadsAsExpected()
    {
        Assert.Equal("LS+RS", GamepadCombo.Format(Combo));
        Assert.Equal("", new AppSettings().ControllerCombo); // off until the user picks one
        Assert.False(new AppSettings().ControllerRumble);
    }

    [Theory]
    [InlineData("Back+Start+LB", "Back+Start+LB")]
    [InlineData(" lb + start + view ", "Back+Start+LB")]
    [InlineData("Menu+RT+Y", "Start+RT+Y")]
    public void ParsesAndFormats(string text, string expected) =>
        Assert.Equal(expected, GamepadCombo.Format(GamepadCombo.Parse(text)!.Value));

    [Theory]
    [InlineData("")]
    [InlineData("A")]          // a single button would fire in games
    [InlineData("A+Banana")]
    [InlineData("A+None")]
    public void RejectsWhatCantBeACombo(string text) => Assert.Null(GamepadCombo.Parse(text));

    [Fact]
    public void FiresAfterHoldingOnce()
    {
        var d = new ComboDetector();
        Assert.False(d.Update(Combo, Combo, T0));
        Assert.False(d.Update(Combo, Combo, T0.AddSeconds(1.4)));
        Assert.True(d.Update(Combo, Combo | GamepadButtons.A, T0.AddSeconds(1.5)));
        Assert.False(d.Update(Combo, Combo, T0.AddSeconds(5)));
    }

    [Fact]
    public void LettingGoStartsOver()
    {
        var d = new ComboDetector();
        d.Update(Combo, Combo, T0);
        d.Update(Combo, GamepadButtons.LS, T0.AddSeconds(1));
        Assert.False(d.Update(Combo, Combo, T0.AddSeconds(2)));
        Assert.True(d.Update(Combo, Combo, T0.AddSeconds(3.5)));
    }

    [Fact]
    public void NoComboNeverFires()
    {
        var d = new ComboDetector();
        d.Update(GamepadButtons.None, Combo, T0);
        Assert.False(d.Update(GamepadButtons.None, Combo, T0.AddSeconds(10)));
    }
}

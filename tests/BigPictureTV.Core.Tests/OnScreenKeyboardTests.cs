using BigPictureTV.Core.Input;
using BigPictureTV.Core.TvMenu;

namespace BigPictureTV.Core.Tests;

public class OnScreenKeyboardTests
{
    [Fact]
    public void OpensOnTheFirstLetterAndTypesIt()
    {
        var keyboard = new OnScreenKeyboard();
        Assert.Equal("q", keyboard.Selected.Label);
        Assert.Equal(new KeyOutput(KeyKind.Text, "q"), keyboard.Press());
    }

    [Fact]
    public void LeftAndRightWrapAroundTheRow()
    {
        var keyboard = new OnScreenKeyboard();
        keyboard.Move(PadAction.Left);
        Assert.Equal("p", keyboard.Selected.Label);
        keyboard.Move(PadAction.Right);
        Assert.Equal("q", keyboard.Selected.Label);
    }

    [Fact]
    public void UpAndDownKeepTheColumnAndStopAtTheEdges()
    {
        var keyboard = new OnScreenKeyboard();
        for (int i = 0; i < 4; i++) keyboard.Move(PadAction.Right); // t
        keyboard.Move(PadAction.Down);
        Assert.Equal("g", keyboard.Selected.Label);
        keyboard.Move(PadAction.Down);
        Assert.Equal("b", keyboard.Selected.Label);
        keyboard.Move(PadAction.Down);
        Assert.Equal(KeyKind.Space, keyboard.Selected.Kind); // columns 4-6
        keyboard.Move(PadAction.Down);
        Assert.Equal(KeyKind.Space, keyboard.Selected.Kind);
        keyboard.Move(PadAction.Up);
        Assert.Equal("n", keyboard.Selected.Label); // back up from the middle of the wide key
        for (int i = 0; i < 5; i++) keyboard.Move(PadAction.Up);
        Assert.Equal(0, keyboard.Row);
    }

    [Fact]
    public void EveryRowIsTenColumnsWide()
    {
        var keyboard = new OnScreenKeyboard();
        foreach (var symbols in new[] { false, true })
        {
            if (symbols) keyboard.ToggleSymbols();
            Assert.All(keyboard.Rows, row => Assert.Equal(OnScreenKeyboard.Columns, row.Sum(k => k.Width)));
        }
    }

    [Fact]
    public void CapitalsLastOneLetter()
    {
        var keyboard = new OnScreenKeyboard();
        keyboard.ToggleCapitals();
        Assert.Equal("Q", keyboard.LabelOf(keyboard.Selected));
        Assert.Equal("Q", keyboard.Press()!.Value.Text);
        Assert.False(keyboard.Capitals);
        Assert.Equal("q", keyboard.Press()!.Value.Text);
    }

    [Fact]
    public void ShiftAndSymbolKeysOnlyChangeTheKeyboard()
    {
        var keyboard = new OnScreenKeyboard();
        var bottom = keyboard.Rows[^1];
        Assert.Null(keyboard.Press(bottom.Single(k => k.Kind == KeyKind.Shift)));
        Assert.True(keyboard.Capitals);
        Assert.Null(keyboard.Press(bottom.Single(k => k.Kind == KeyKind.Symbols)));
        Assert.True(keyboard.ShowingSymbols);
        Assert.Equal("@", keyboard.Selected.Label);
        Assert.Equal("@", keyboard.Press()!.Value.Text); // capitals don't touch symbols
    }

    [Fact]
    public void SpecialKeysAskForTheirAction()
    {
        var keyboard = new OnScreenKeyboard();
        var bottom = keyboard.Rows[^1];
        Assert.Equal(new KeyOutput(KeyKind.Text, " "), keyboard.Press(bottom.Single(k => k.Kind == KeyKind.Space)));
        Assert.Equal(new KeyOutput(KeyKind.Backspace), keyboard.Press(bottom.Single(k => k.Kind == KeyKind.Backspace)));
        Assert.Equal(new KeyOutput(KeyKind.Enter), keyboard.Press(bottom.Single(k => k.Kind == KeyKind.Enter)));
    }

    [Fact]
    public void ResetGoesBackToLowerCaseLetters()
    {
        var keyboard = new OnScreenKeyboard();
        keyboard.ToggleCapitals();
        keyboard.ToggleSymbols();
        keyboard.Move(PadAction.Down);
        keyboard.Reset();
        Assert.False(keyboard.Capitals);
        Assert.False(keyboard.ShowingSymbols);
        Assert.Equal("q", keyboard.Selected.Label);
    }

    [Fact]
    public void RightStickClickOpensTheKeyboard()
    {
        var mapper = new PadMapper();
        var t = new DateTime(2026, 10, 7, 20, 0, 0, DateTimeKind.Utc);
        Assert.Equal(new[] { PadAction.Keyboard }, mapper.Update(GamepadButtons.RS, t));
    }
}

using BigPictureTV.Core.Input;
using BigPictureTV.Core.TvMenu;

namespace BigPictureTV.Core.Tests;

public class TvMenuTests
{
    static readonly DateTime T0 = new(2026, 10, 7, 20, 0, 0, DateTimeKind.Utc);

    [Fact]
    public void DefaultTilesAreUsableAndSurviveASave()
    {
        var settings = new AppSettings();
        Assert.True(settings.ShortcutOpensTvMenu);
        Assert.Equal(new[] { "YouTube", "Crunchyroll", "Big Picture", "Desktop" }, settings.TvMenuApps.Select(a => a.ToString()));
        Assert.All(settings.TvMenuApps, a => Assert.True(a.IsUsable));

        var file = Path.Combine(Path.GetTempPath(), $"bptv-{Guid.NewGuid():N}.json");
        try
        {
            settings.TvMenuApps.RemoveAt(1);
            settings.Save(file);
            var loaded = AppSettings.Load(file);
            Assert.Equal(3, loaded.TvMenuApps.Count); // the saved list, not defaults added on top
            Assert.Equal(TvApp.SmartTvUserAgent, loaded.TvMenuApps[0].UserAgent);
            Assert.Equal(TvAppKind.BigPicture, loaded.TvMenuApps[1].Kind);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("https://www.crunchyroll.com", true)]
    [InlineData("http://192.168.0.10:8096", true)]
    [InlineData("file:///C:/Windows/notepad.exe", false)]
    [InlineData("crunchyroll.com", false)]
    [InlineData("", false)]
    public void WebTilesNeedAWebAddress(string target, bool usable) =>
        Assert.Equal(usable, new TvApp { Target = target }.IsUsable);

    [Fact]
    public void BrowserGetsAnAppWindowWithItsOwnProfile()
    {
        var app = TvApp.Defaults()[0];
        string profile = BrowserCommand.ProfileDir(@"C:\Users\x\AppData\Local\BigPictureTV", app);
        Assert.EndsWith("youtube", profile);
        string args = BrowserCommand.Arguments(app, profile);
        Assert.Contains($"\"--user-data-dir={profile}\"", args);
        Assert.Contains("--start-fullscreen", args);
        Assert.Contains($"\"--user-agent={TvApp.SmartTvUserAgent}\"", args);
        Assert.EndsWith("\"--app=https://www.youtube.com/tv\"", args);

        var plain = new TvApp { Name = "Mi Página!", Target = "https://example.com/\"x" };
        Assert.EndsWith(Path.Combine("Browser", "mi-página"), BrowserCommand.ProfileDir("d", plain));
        Assert.DoesNotContain("--user-agent", BrowserCommand.Arguments(plain, "p"));
        Assert.EndsWith("\"--app=https://example.com/x\"", BrowserCommand.Arguments(plain, "p"));
        Assert.EndsWith(Path.Combine("Browser", "web"), BrowserCommand.ProfileDir("d", new TvApp { Name = "!!" }));
    }

    [Theory]
    // Four columns, six tiles:  0 1 2 3
    //                           4 5
    [InlineData(0, PadAction.Left, 0)]
    [InlineData(0, PadAction.Right, 1)]
    [InlineData(3, PadAction.Right, 3)]
    [InlineData(5, PadAction.Right, 5)]
    [InlineData(1, PadAction.Down, 5)]
    [InlineData(3, PadAction.Down, 5)]
    [InlineData(5, PadAction.Up, 1)]
    [InlineData(4, PadAction.Down, 4)]
    [InlineData(0, PadAction.Up, 0)]
    public void GridMovesStopAtTheEdges(int from, PadAction direction, int to) =>
        Assert.Equal(to, GridNav.Move(from, 6, 4, direction));

    [Fact]
    public void GridHandlesFewTiles()
    {
        Assert.Equal(1, GridNav.Columns(1));
        Assert.Equal(4, GridNav.Columns(9));
        Assert.Equal(0, GridNav.Move(0, 0, 4, PadAction.Right));
        Assert.Equal(1, GridNav.Move(7, 2, 4, PadAction.Up)); // a stale index is clamped
    }

    [Fact]
    public void PressesActOnceAndDirectionsRepeat()
    {
        var mapper = new PadMapper();
        Assert.Equal(new[] { PadAction.Accept }, mapper.Update(GamepadButtons.A, T0));
        Assert.Empty(mapper.Update(GamepadButtons.A, T0.AddSeconds(1))); // held: once
        Assert.Empty(mapper.Update(GamepadButtons.None, T0.AddSeconds(1.1)));

        var t = T0.AddSeconds(2);
        Assert.Equal(new[] { PadAction.Down }, mapper.Update(GamepadButtons.Down, t));
        Assert.Empty(mapper.Update(GamepadButtons.Down, t + PadMapper.RepeatDelay - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(new[] { PadAction.Down }, mapper.Update(GamepadButtons.Down, t + PadMapper.RepeatDelay));
        Assert.Empty(mapper.Update(GamepadButtons.Down, t + PadMapper.RepeatDelay + TimeSpan.FromMilliseconds(50)));
        Assert.Equal(new[] { PadAction.Down }, mapper.Update(GamepadButtons.Down, t + PadMapper.RepeatDelay + PadMapper.RepeatEvery));

        // Changing direction acts right away.
        Assert.Equal(new[] { PadAction.Right }, mapper.Update(GamepadButtons.Right, t + TimeSpan.FromSeconds(1)));
    }

    [Fact]
    public void ButtonsHeldTogetherAreACombo()
    {
        var mapper = new PadMapper();
        Assert.Empty(mapper.Update(GamepadButtons.A | GamepadButtons.B, T0));
        Assert.Empty(mapper.Update(GamepadButtons.LS | GamepadButtons.RS, T0.AddSeconds(1)));
    }

    [Fact]
    public void HoldingViewClosesTheAppOnce()
    {
        var mapper = new PadMapper();
        Assert.Empty(mapper.Update(GamepadButtons.Back, T0));
        Assert.Empty(mapper.Update(GamepadButtons.Back, T0 + PadMapper.CloseHold - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(new[] { PadAction.Close }, mapper.Update(GamepadButtons.Back, T0 + PadMapper.CloseHold));
        Assert.Empty(mapper.Update(GamepadButtons.Back, T0 + TimeSpan.FromSeconds(5)));
        Assert.Empty(mapper.Update(GamepadButtons.None, T0 + TimeSpan.FromSeconds(6)));

        // View together with other buttons (a combo) never closes.
        Assert.Empty(mapper.Update(GamepadButtons.Back | GamepadButtons.Start, T0 + TimeSpan.FromSeconds(7)));
        Assert.Empty(mapper.Update(GamepadButtons.Back | GamepadButtons.Start, T0 + TimeSpan.FromSeconds(9)));
    }

    [Theory]
    [InlineData("Esc", KeyModifiers.None, 0x1Bu)]
    [InlineData("alt+left", KeyModifiers.Alt, 0x25u)]
    [InlineData("0xB3", KeyModifiers.None, 0xB3u)]
    public void SingleKeysParseForSending(string text, KeyModifiers mods, uint key)
    {
        Assert.Equal(new Hotkey(mods, key), Hotkey.ParseAny(text));
    }

    [Theory]
    [InlineData("")]
    [InlineData("Ctrl")]
    [InlineData("Esc+Enter")]
    public void BadKeysDontParse(string text) => Assert.Null(Hotkey.ParseAny(text));

    [Fact]
    public void ShortcutsStillNeedAModifier() => Assert.Null(Hotkey.Parse("Esc"));
}

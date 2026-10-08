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
        Assert.Equal(new[] { "YouTube", "Crunchyroll", "Spotify", "YouTube Music", "Big Picture", "Desktop" }, settings.TvMenuApps.Select(a => a.ToString()));
        Assert.All(settings.TvMenuApps, a => Assert.True(a.IsUsable));

        var file = Path.Combine(Path.GetTempPath(), $"bptv-{Guid.NewGuid():N}.json");
        try
        {
            settings.TvMenuApps.RemoveRange(1, 3);
            settings.TvMenuVersion = TvApp.DefaultsVersion;
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
        Assert.EndsWith(Path.Combine("Browser", "shared"), BrowserCommand.ProfileDir("d", plain));
        Assert.EndsWith(Path.Combine("Browser", "mi-página"), BrowserCommand.NamedProfileDir("d", plain));
        Assert.DoesNotContain("--user-agent", BrowserCommand.Arguments(plain, "p"));
        Assert.EndsWith("\"--app=https://example.com/x\"", BrowserCommand.Arguments(plain, "p"));
        Assert.EndsWith(Path.Combine("Browser", "web"), BrowserCommand.NamedProfileDir("d", new TvApp { Name = "!!" }));
    }

    [Fact]
    public void PlainPagesShareAProfileButTvPagesAndMusicKeepTheirOwn()
    {
        var byName = TvApp.Defaults().ToDictionary(a => a.Name);
        Assert.True(BrowserCommand.SharesProfile(byName["Crunchyroll"]));
        Assert.False(BrowserCommand.SharesProfile(byName["YouTube"]));       // TV interface
        Assert.False(BrowserCommand.SharesProfile(byName["YouTube Music"])); // its own volume
        Assert.False(BrowserCommand.SharesProfile(byName["Spotify"]));       // a program
        Assert.EndsWith(Path.Combine("Browser", "youtube-music"), BrowserCommand.ProfileDir("d", byName["YouTube Music"]));
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
    public void XClosesAndViewIsLeftToTheMenuButton()
    {
        var mapper = new PadMapper();
        Assert.Equal(new[] { PadAction.Option }, mapper.Update(GamepadButtons.X, T0));
        Assert.Empty(mapper.Update(GamepadButtons.Back, T0.AddSeconds(1)));
        Assert.Empty(mapper.Update(GamepadButtons.Back, T0.AddSeconds(5)));
    }

    [Fact]
    public void TheNavigationExtensionShipsWithTheApp()
    {
        Assert.Contains("\"spatial-nav.js\"", SpatialNav.Read("manifest.json"));
        Assert.Contains("addEventListener('keydown'", SpatialNav.Read("spatial-nav.js"));
        var dir = Path.Combine(Path.GetTempPath(), $"bptv-ext-{Guid.NewGuid():N}");
        try
        {
            SpatialNav.Write(dir);
            Assert.True(File.Exists(Path.Combine(dir, "manifest.json")));
            Assert.True(File.Exists(Path.Combine(dir, "spatial-nav.js")));
            // Every file the manifest names ships, icons included (the store wants a 128 px one).
            foreach (var named in System.Text.RegularExpressions.Regex.Matches(SpatialNav.Read("manifest.json"), "\"([\\w-]+\\.(?:js|png))\"").Select(m => m.Groups[1].Value))
                Assert.True(File.Exists(Path.Combine(dir, named)), named);
            Assert.Equal(new byte[] { 0x89, (byte)'P', (byte)'N', (byte)'G' }, File.ReadAllBytes(Path.Combine(dir, "icon128.png"))[..4]);
            SpatialNav.Write(dir); // again, unchanged: fine
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void OnlyTvPagesGetDevToolsAndTheOthersTheExtension()
    {
        var tv = TvApp.Defaults().First(a => a.Name == "YouTube");
        var plain = new TvApp { Name = "X", Kind = TvAppKind.Web, Target = "https://example.com" };
        Assert.Contains("--remote-debugging-port=0", BrowserCommand.Arguments(tv, "p", "ext"));
        Assert.DoesNotContain("--load-extension", BrowserCommand.Arguments(tv, "p", "ext"));
        Assert.DoesNotContain("--remote-debugging-port", BrowserCommand.Arguments(plain, "p", "ext"));
        Assert.Contains("\"--load-extension=ext\"", BrowserCommand.Arguments(plain, "p", "ext"));
    }

    [Fact]
    public void TriggersRepeatWhileHeld()
    {
        var mapper = new PadMapper();
        Assert.Equal(new[] { PadAction.PageDown }, mapper.Update(GamepadButtons.RT, T0));
        Assert.Empty(mapper.Update(GamepadButtons.RT, T0 + PadMapper.RepeatDelay - TimeSpan.FromMilliseconds(1)));
        Assert.Equal(new[] { PadAction.PageDown }, mapper.Update(GamepadButtons.RT, T0 + PadMapper.RepeatDelay));
        Assert.Equal(new[] { PadAction.PageDown },
            mapper.Update(GamepadButtons.RT, T0 + PadMapper.RepeatDelay + PadMapper.TriggerRepeatEvery));
        Assert.Empty(mapper.Update(GamepadButtons.None, T0.AddSeconds(2)));
        Assert.Equal(new[] { PadAction.PageUp }, mapper.Update(GamepadButtons.LT, T0.AddSeconds(3)));
    }

    [Fact]
    public void ATapOfTheMenuButtonFiresOnRelease()
    {
        var tap = new TapDetector();
        Assert.False(tap.Update(GamepadButtons.Back, GamepadButtons.Back, T0));
        Assert.True(tap.Update(GamepadButtons.Back, GamepadButtons.None, T0.AddMilliseconds(200)));
        Assert.False(tap.Update(GamepadButtons.Back, GamepadButtons.None, T0.AddMilliseconds(300))); // once
    }

    [Fact]
    public void HoldsAndCombosAreNotTaps()
    {
        var tap = new TapDetector();
        tap.Update(GamepadButtons.Back, GamepadButtons.Back, T0);
        Assert.False(tap.Update(GamepadButtons.Back, GamepadButtons.None, T0 + TapDetector.MaxTap + TimeSpan.FromMilliseconds(1)));

        // Back+Start (a combo), let go in any order.
        tap.Update(GamepadButtons.Back, GamepadButtons.Back, T0.AddSeconds(2));
        tap.Update(GamepadButtons.Back, GamepadButtons.Back | GamepadButtons.Start, T0.AddSeconds(2.1));
        tap.Update(GamepadButtons.Back, GamepadButtons.Back, T0.AddSeconds(2.2));
        Assert.False(tap.Update(GamepadButtons.Back, GamepadButtons.None, T0.AddSeconds(2.3)));

        // Pressed while another button was already down.
        tap.Update(GamepadButtons.Back, GamepadButtons.A | GamepadButtons.Back, T0.AddSeconds(3));
        Assert.False(tap.Update(GamepadButtons.Back, GamepadButtons.A, T0.AddSeconds(3.1)));

        // Off.
        Assert.False(tap.Update(GamepadButtons.None, GamepadButtons.None, T0.AddSeconds(4)));
    }

    [Theory]
    [InlineData("Back", GamepadButtons.Back)]
    [InlineData(" share ", GamepadButtons.Back)]
    [InlineData("Options", GamepadButtons.Start)]
    [InlineData("L3", GamepadButtons.LS)]
    [InlineData("A", GamepadButtons.A)]
    [InlineData("Share+R1", GamepadButtons.Back | GamepadButtons.RB)]
    public void MenuButtonNames(string text, GamepadButtons expected) => Assert.Equal(expected, GamepadCombo.ParseAny(text));

    [Theory]
    [InlineData("")]
    [InlineData("Back+Nope")]
    public void UnknownMenuButtonsAreNone(string text) => Assert.Null(GamepadCombo.ParseAny(text));

    [Fact]
    public void ShareOpensTheMenuByDefault()
    {
        var settings = new AppSettings();
        Assert.Equal(GamepadButtons.Back, GamepadCombo.ParseAny(settings.ControllerMenuButton));
        Assert.Equal(0, settings.ControllerMenuHold);
    }

    [Fact]
    public void SeveralButtonsTappedTogetherCount()
    {
        var tap = new TapDetector();
        var chord = GamepadButtons.LB | GamepadButtons.RB;
        tap.Update(chord, GamepadButtons.LB, T0);
        tap.Update(chord, chord, T0.AddMilliseconds(80));
        tap.Update(chord, GamepadButtons.RB, T0.AddMilliseconds(250));
        Assert.True(tap.Update(chord, GamepadButtons.None, T0.AddMilliseconds(300)));
    }

    [Fact]
    public void ABindingWithNoHoldIsATap()
    {
        var d = new BindingDetector();
        Assert.False(d.Update(GamepadButtons.Back, 0, GamepadButtons.Back, T0));
        Assert.False(d.Update(GamepadButtons.Back, 0, GamepadButtons.Back, T0.AddMilliseconds(150)));
        Assert.True(d.Update(GamepadButtons.Back, 0, GamepadButtons.None, T0.AddMilliseconds(200)));
    }

    [Fact]
    public void ABindingWithAHoldFiresOnceAfterThatTime()
    {
        var d = new BindingDetector();
        var combo = GamepadButtons.Back | GamepadButtons.Down;
        Assert.False(d.Update(combo, 3, combo, T0));
        Assert.False(d.Update(combo, 3, combo, T0.AddSeconds(2.9)));
        Assert.True(d.Update(combo, 3, combo, T0.AddSeconds(3)));
        Assert.False(d.Update(combo, 3, combo, T0.AddSeconds(4)));
        Assert.False(d.Update(combo, 3, GamepadButtons.None, T0.AddSeconds(4.1))); // no tap on release
    }

    [Fact]
    public void AnEmptyBindingNeverFires()
    {
        var d = new BindingDetector();
        Assert.False(d.Update(GamepadButtons.None, 0, GamepadButtons.Back, T0));
        Assert.False(d.Update(GamepadButtons.None, 0, GamepadButtons.None, T0.AddMilliseconds(100)));
    }

    [Fact]
    public void TileKeysTellTilesApart()
    {
        var apps = TvApp.Defaults();
        Assert.Equal(apps.Count, apps.Select(a => a.Key).Distinct().Count());
        Assert.Equal(apps[0].Key, AppSettingsRoundTrip(apps)[0].Key);
    }

    static List<TvApp> AppSettingsRoundTrip(List<TvApp> apps)
    {
        var file = Path.Combine(Path.GetTempPath(), $"bptv-{Guid.NewGuid():N}.json");
        try
        {
            new AppSettings { TvMenuApps = apps }.Save(file);
            return AppSettings.Load(file).TvMenuApps;
        }
        finally
        {
            File.Delete(file);
        }
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
    [Theory]
    [InlineData("Mozilla/5.0 (SMART-TV; Linux; Tizen 6.0) AppleWebKit/537.36 (KHTML, like Gecko) 76.0.3809.146/6.0 TV Safari/537.36")]
    [InlineData("Mozilla/5.0 (Linux; Android 12) Cobalt/22.2.3-gold (PS5)")]
    public void OldYouTubeUserAgentIsReplacedOnLoad(string tizen)
    {
        var file = Path.Combine(Path.GetTempPath(), $"bptv-{Guid.NewGuid():N}.json");
        try
        {
            var settings = new AppSettings();
            settings.TvMenuApps[0].UserAgent = tizen;
            settings.TvMenuApps[1].UserAgent = "Custom/1.0";
            settings.Save(file);
            var loaded = AppSettings.Load(file);
            Assert.Equal(TvApp.SmartTvUserAgent, loaded.TvMenuApps[0].UserAgent);
            Assert.Equal("Custom/1.0", loaded.TvMenuApps[1].UserAgent); // the user's own choice stays

            File.WriteAllText(file, "{ \"TvMenuApps\": null }");
            Assert.Equal(TvApp.Defaults().Count, AppSettings.Load(file).TvMenuApps.Count);
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Theory]
    [InlineData("\"C:\\Edge\\msedge.exe\" \"--user-data-dir=C:\\D\\Browser\\youtube\" --no-first-run \"--app=https://www.youtube.com/tv\"", true)]
    [InlineData("C:\\Edge\\msedge.exe --user-data-dir=c:\\d\\browser\\YOUTUBE --flag", true)]
    [InlineData("\"C:\\Edge\\msedge.exe\" --type=renderer \"--user-data-dir=C:\\D\\Browser\\youtube\"", false)]
    [InlineData("\"C:\\Edge\\msedge.exe\" \"--user-data-dir=C:\\D\\Browser\\youtube2\"", false)]
    [InlineData("\"C:\\Edge\\msedge.exe\" --profile-directory=Default", false)]
    [InlineData(null, false)]
    public void FindsTheBrowserStartedForATile(string? commandLine, bool expected) =>
        Assert.Equal(expected, BrowserCommand.IsMainProcessFor(commandLine, "C:\\D\\Browser\\youtube"));

    [Fact]
    public void MusicTilesReachSettingsSavedBeforeThemOnce()
    {
        var file = Path.Combine(Path.GetTempPath(), $"bptv-{Guid.NewGuid():N}.json");
        try
        {
            var old = new AppSettings { TvMenuApps = TvApp.Defaults().Where(a => !a.Name.Contains("Spotify") && !a.Name.Contains("Music")).ToList() };
            Assert.Equal(0, old.TvMenuVersion);
            old.Save(file);

            var loaded = AppSettings.Load(file);
            Assert.Equal(new[] { "YouTube", "Crunchyroll", "Spotify", "YouTube Music", "Big Picture", "Desktop" }, loaded.TvMenuApps.Select(a => a.ToString()));
            Assert.Equal(TvApp.DefaultsVersion, AppSettings.Load(file).TvMenuVersion); // written back

            // Removed afterwards, it stays removed.
            loaded.TvMenuApps.RemoveAll(a => a.Name == "Spotify");
            loaded.Save(file);
            Assert.DoesNotContain(AppSettings.Load(file).TvMenuApps, a => a.Name == "Spotify");
        }
        finally
        {
            File.Delete(file);
        }
    }

    [Fact]
    public void NewDefaultsDontDuplicateATileAlreadyThere()
    {
        var apps = new List<TvApp> { new() { Name = "My music", Target = "https://music.youtube.com" } };
        TvApp.AddNewDefaults(apps, 0);
        Assert.Equal(new[] { "My music", "Spotify" }, apps.Select(a => a.ToString()));
    }

    [Fact]
    public void SpotifyOpensItsAppWithTheWebAsFallback()
    {
        var spotify = TvApp.Defaults().Single(a => a.Name == "Spotify");
        Assert.Equal(TvAppKind.Program, spotify.Kind);
        Assert.Equal("spotify:", spotify.Target);
        Assert.True(new TvApp { Target = spotify.Fallback }.IsUsable);
    }

    [Fact]
    public void RightStickTurnsTheVolumeAndRepeatsWhileHeld()
    {
        var mapper = new PadMapper();
        Assert.Equal(new[] { PadAction.VolumeUp }, mapper.Update(GamepadButtons.RStickUp, T0));
        Assert.Empty(mapper.Update(GamepadButtons.RStickUp, T0.AddMilliseconds(100)));
        Assert.Equal(new[] { PadAction.VolumeUp }, mapper.Update(GamepadButtons.RStickUp, T0.AddMilliseconds(250)));
        Assert.Equal(new[] { PadAction.VolumeDown }, mapper.Update(GamepadButtons.RStickDown, T0.AddMilliseconds(300)));
        // Held with a button, it is no combo: the button still counts.
        Assert.Equal(new[] { PadAction.Accept }, mapper.Update(GamepadButtons.RStickDown | GamepadButtons.A, T0.AddMilliseconds(310)));
    }

    [Fact]
    public void TheRightStickIsNeverPartOfACombo() =>
        Assert.Null(GamepadCombo.ParseAny("RStickUp+A"));

    [Fact]
    public void MusicTilesAreKnownByName()
    {
        var apps = TvApp.Defaults();
        Assert.Equal(new[] { "Spotify", "YouTube Music" }, apps.Where(a => a.IsMusic).Select(a => a.Name));
    }

    [Theory]
    [InlineData("msedge.exe --type=utility --utility-sub-type=audio.mojom.AudioService --user-data-dir=\"C:\\D\\Browser\\YouTube Music\" --field-trial", true)]
    [InlineData("msedge.exe --user-data-dir=\"C:\\D\\Browser\\YouTube Music\"", true)]
    [InlineData("msedge.exe --type=utility --user-data-dir=\"C:\\D\\Browser\\YouTube\"", false)]
    [InlineData(null, false)]
    public void HelperProcessesBelongToTheirProfile(string? commandLine, bool expected) =>
        Assert.Equal(expected, BrowserCommand.UsesProfile(commandLine, "C:\\D\\Browser\\YouTube Music"));
}

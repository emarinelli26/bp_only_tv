using BigPictureTV.Core.Detection;
using BigPictureTV.Core.Display;

namespace BigPictureTV.Core.Tests;

public sealed class DisplayManagerTests : IDisposable
{
    readonly string _dir = Path.Combine(Path.GetTempPath(), "bptv-tests-" + Guid.NewGuid().ToString("N"));
    readonly FakeDisplayConfig _display = new();
    readonly LayoutStore _store;
    readonly ListLog _log = new();
    readonly DisplayManager _manager;

    public DisplayManagerTests()
    {
        _store = new LayoutStore(Path.Combine(_dir, "saved-layout.json"));
        _display.Displays.Add(Displays.Monitor);
        _display.Displays.Add(Displays.LgTv);
        _manager = new DisplayManager(_display, _store, ds => TvSelector.Select(ds, new AppSettings()), _log);
    }

    public void Dispose() { try { Directory.Delete(_dir, true); } catch (IOException) { } }

    [Fact]
    public void SwitchSavesTheDesktopOnceAndAppliesTvOnly()
    {
        Assert.True(_manager.SwitchToTv());
        Assert.True(_store.HasSaved);
        Assert.Single(_display.Applied);
        Assert.False(_display.Applied[0].Save);

        // A second switch must not overwrite the saved desktop with the TV-only layout.
        _display.Active = new Layout { Paths = new PATH_INFO[3] };
        _manager.SwitchToTv();
        Assert.Equal(2, _store.Load().Paths.Length);
    }

    [Fact]
    public void SwitchFailsWhenNoTvIsFound()
    {
        _display.Displays.RemoveAt(1);
        Assert.False(_manager.SwitchToTv());
        Assert.Empty(_display.Applied);
        Assert.False(_store.HasSaved);
    }

    [Fact]
    public void RestoreAppliesTheSavedLayoutAndForgetsIt()
    {
        _manager.SwitchToTv();
        _manager.RestoreDesktop();
        Assert.True(_display.Applied[^1].Save);
        Assert.Equal(2, _display.Applied[^1].Layout.Paths.Length);
        Assert.False(_store.HasSaved);
        Assert.Equal(0, _display.Extends);
    }

    [Fact]
    public void RestoreFallsBackToExtendWhenTheSavedLayoutIsRejected()
    {
        _manager.SwitchToTv();
        _display.ApplyResult = 87; // ERROR_INVALID_PARAMETER, e.g. adapter ids changed
        _manager.RestoreDesktop();
        Assert.Equal(1, _display.Extends);
        Assert.False(_store.HasSaved);
    }

    [Fact]
    public void RestoreKeepsTheSavedLayoutIfEverythingFails()
    {
        _manager.SwitchToTv();
        _display.ApplyResult = 87;
        _display.ExtendResult = 31;
        _manager.RestoreDesktop();
        Assert.True(_store.HasSaved);
    }

    [Fact]
    public void IsOnTvReadsTheRealDisplays()
    {
        _manager.SwitchToTv();
        _display.Displays[0] = Displays.Monitor with { Active = false };
        Assert.True(_manager.IsOnTv());

        _display.Displays[0] = Displays.Monitor; // Windows turned the monitor back on
        Assert.False(_manager.IsOnTv());

        _display.Displays[0] = Displays.Monitor;
        _display.Displays[1] = Displays.LgTv with { Active = false }; // only the monitor
        Assert.False(_manager.IsOnTv());
    }

    [Fact]
    public void IsOnTvFollowsTheDisplayWeSwitchedToEvenIfTheChoiceChanges()
    {
        var settings = new AppSettings();
        var manager = new DisplayManager(_display, _store, ds => TvSelector.Select(ds, settings), _log);
        manager.SwitchToTv();
        _display.Displays[0] = Displays.Monitor with { Active = false };
        settings.TvDevicePath = Displays.Monitor.DevicePath; // user picks another TV while on the TV
        Assert.True(manager.IsOnTv());
    }

    DisplayManager ManagerWith(TvLayout layout) =>
        new(_display, _store, ds => TvSelector.Select(ds, new AppSettings()), _log, () => layout);

    [Fact]
    public void TvPrimaryKeepsEveryDisplayAndMovesTheTvToTheOrigin()
    {
        var m = ManagerWith(TvLayout.TvPrimary);
        Assert.True(m.SwitchToTv());
        Assert.True(_store.HasSaved);
        Assert.Equal(2, _display.Applied.Single().Layout.Paths.Length);
        Assert.Equal(0, _display.Extends);
    }

    [Fact]
    public void TvPrimaryTurnsAnInactiveTvOnFirst()
    {
        _display.Displays[1] = Displays.LgTv with { Active = false };
        _display.OnExtend = () => _display.Displays[1] = Displays.LgTv;
        var m = ManagerWith(TvLayout.TvPrimary);
        Assert.True(m.SwitchToTv());
        Assert.Equal(1, _display.Extends);
        Assert.Single(_display.Applied);
    }

    [Fact]
    public void TvPrimaryFailsIfTheTvNeverComesOn()
    {
        _display.Displays[1] = Displays.LgTv with { Active = false };
        Assert.False(ManagerWith(TvLayout.TvPrimary).SwitchToTv());
        Assert.Empty(_display.Applied);
    }

    [Fact]
    public void DuplicateUsesTheCloneTopology()
    {
        var m = ManagerWith(TvLayout.Duplicate);
        Assert.True(m.SwitchToTv());
        Assert.Equal(1, _display.Clones);
        Assert.True(_store.HasSaved);
    }

    [Fact]
    public void SwitchingWhenAlreadyThereChangesNothing()
    {
        _display.Displays[0] = Displays.Monitor with { Primary = false };
        _display.Displays[1] = Displays.LgTv with { Primary = true };
        Assert.True(ManagerWith(TvLayout.TvPrimary).SwitchToTv());
        Assert.Empty(_display.Applied);
        Assert.False(_store.HasSaved);
    }

    [Fact]
    public void RecognizesEachLayout()
    {
        string tv = Displays.LgTv.DevicePath;
        var tvOnly = new[] { Displays.Monitor with { Active = false }, Displays.LgTv with { Primary = true } };
        var extendedMonitorPrimary = new[] { Displays.Monitor, Displays.LgTv };
        var extendedTvPrimary = new[] { Displays.Monitor with { Primary = false }, Displays.LgTv with { Primary = true } };
        var cloned = new[] { Displays.Monitor, Displays.LgTv with { Primary = true } };

        Assert.True(DisplayManager.IsIn(TvLayout.TvOnly, tvOnly, tv));
        Assert.False(DisplayManager.IsIn(TvLayout.TvOnly, extendedTvPrimary, tv));

        Assert.True(DisplayManager.IsIn(TvLayout.TvPrimary, extendedTvPrimary, tv));
        Assert.False(DisplayManager.IsIn(TvLayout.TvPrimary, extendedMonitorPrimary, tv));

        Assert.True(DisplayManager.IsIn(TvLayout.Duplicate, cloned, tv));
        Assert.False(DisplayManager.IsIn(TvLayout.Duplicate, extendedTvPrimary, tv));

        var tvOff = new[] { Displays.Monitor, Displays.LgTv with { Active = false } };
        Assert.False(DisplayManager.IsIn(TvLayout.TvPrimary, tvOff, tv));
    }

    [Fact]
    public void SettingsKeepTheLayoutAsText()
    {
        string file = Path.Combine(_dir, "settings.json");
        new AppSettings { Layout = TvLayout.Duplicate, FirstRunDone = true }.Save(file);
        Assert.Contains("\"Duplicate\"", File.ReadAllText(file));
        var loaded = AppSettings.Load(file);
        Assert.Equal(TvLayout.Duplicate, loaded.Layout);
        Assert.True(loaded.FirstRunDone);

        var copy = loaded.Clone();
        copy.ExtraProcesses.Add("retroarch");
        Assert.Empty(loaded.ExtraProcesses);
    }

    [Fact]
    public void NormalizesExtraProcessNames()
    {
        Assert.Equal(new[] { "retroarch", "dolphin", "pcsx2" },
            BigPictureWatcher.NormalizeProcessNames(new[] { "retroarch.exe, dolphin", "", "pcsx2", "RetroArch" }));
    }
}

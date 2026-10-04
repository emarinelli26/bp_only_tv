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
    public void IsTvOnlyReadsTheRealDisplays()
    {
        _manager.SwitchToTv();
        _display.Displays[0] = Displays.Monitor with { Active = false };
        Assert.True(_manager.IsTvOnly());

        _display.Displays[0] = Displays.Monitor; // Windows turned the monitor back on
        Assert.False(_manager.IsTvOnly());

        _display.Displays[0] = Displays.Monitor;
        _display.Displays[1] = Displays.LgTv with { Active = false }; // only the monitor
        Assert.False(_manager.IsTvOnly());
    }

    [Fact]
    public void IsTvOnlyFollowsTheDisplayWeSwitchedToEvenIfTheChoiceChanges()
    {
        var settings = new AppSettings();
        var manager = new DisplayManager(_display, _store, ds => TvSelector.Select(ds, settings), _log);
        manager.SwitchToTv();
        _display.Displays[0] = Displays.Monitor with { Active = false };
        settings.TvDevicePath = Displays.Monitor.DevicePath; // user picks another TV while on the TV
        Assert.True(manager.IsTvOnly());
    }

    [Fact]
    public void NormalizesExtraProcessNames()
    {
        Assert.Equal(new[] { "retroarch", "dolphin", "pcsx2" },
            BigPictureWatcher.NormalizeProcessNames(new[] { "retroarch.exe, dolphin", "", "pcsx2", "RetroArch" }));
    }
}

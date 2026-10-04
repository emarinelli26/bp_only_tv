namespace BigPictureTV.Core.Tests;

public class ModeControllerTests
{
    static readonly DateTime T0 = new(2026, 10, 4, 12, 0, 0, DateTimeKind.Utc);
    readonly FakeSwitcher _sw = new();
    readonly ModeController _c;

    public ModeControllerTests()
    {
        _c = new ModeController(_sw, new ListLog()) { Grace = TimeSpan.FromSeconds(5) };
    }

    [Fact]
    public void OpeningBigPictureSwitchesToTv()
    {
        _c.Tick(false, T0);
        Assert.Equal(0, _sw.Switches);
        _c.Tick(true, T0.AddSeconds(2));
        Assert.Equal(DisplayMode.TvAuto, _c.Mode);
        Assert.Equal(1, _sw.Switches);
    }

    [Fact]
    public void ClosingWaitsForTheGracePeriod()
    {
        _c.Tick(true, T0);
        _c.Tick(false, T0.AddSeconds(1));
        _c.Tick(false, T0.AddSeconds(5));
        Assert.Equal(DisplayMode.TvAuto, _c.Mode);
        _c.Tick(false, T0.AddSeconds(6));
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
        Assert.Equal(1, _sw.Restores);
    }

    [Fact]
    public void ClosingAnnouncesTheWaitOnce()
    {
        var waits = new List<TimeSpan>();
        _c.LeavingTvSoon += waits.Add;
        _c.Tick(true, T0);
        _c.Tick(false, T0.AddSeconds(1));
        _c.Tick(false, T0.AddSeconds(3));
        Assert.Equal(new[] { TimeSpan.FromSeconds(5) }, waits);
    }

    [Fact]
    public void ReopeningWithinTheGraceKeepsTheTv()
    {
        _c.Tick(true, T0);
        _c.Tick(false, T0.AddSeconds(1));
        _c.Tick(true, T0.AddSeconds(3));
        _c.Tick(false, T0.AddSeconds(7));
        Assert.Equal(DisplayMode.TvAuto, _c.Mode);
        Assert.Equal(0, _sw.Restores);
        Assert.Equal(1, _sw.Switches);
    }

    [Fact]
    public void AFailedSwitchIsNotRetriedUntilBigPictureReopens()
    {
        _sw.SwitchSucceeds = false;
        _c.Tick(true, T0);
        _c.Tick(true, T0.AddSeconds(2));
        Assert.Equal(1, _sw.Switches);
        Assert.Equal(DisplayMode.Desktop, _c.Mode);

        _sw.SwitchSucceeds = true;
        _c.Tick(false, T0.AddSeconds(4));
        _c.Tick(true, T0.AddSeconds(6));
        Assert.Equal(2, _sw.Switches);
        Assert.Equal(DisplayMode.TvAuto, _c.Mode);
    }

    [Fact]
    public void PausedIgnoresBigPicture()
    {
        _c.SetPaused(true);
        _c.Tick(true, T0);
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
        _c.SetPaused(false);
        _c.Tick(true, T0.AddSeconds(2));
        Assert.Equal(DisplayMode.TvAuto, _c.Mode);
    }

    [Fact]
    public void ManualModeIgnoresBigPictureClosing()
    {
        _c.Toggle(bigPictureOpen: false);
        Assert.Equal(DisplayMode.TvManual, _c.Mode);
        _c.Tick(false, T0);
        _c.Tick(false, T0.AddMinutes(5));
        Assert.Equal(DisplayMode.TvManual, _c.Mode);
        _c.Toggle(bigPictureOpen: false);
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
        Assert.Equal(1, _sw.Restores);
    }

    [Fact]
    public void TogglingOffWhileBigPictureIsOpenStaysOff()
    {
        _c.Tick(true, T0);
        _c.Toggle(bigPictureOpen: true);
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
        _c.Tick(true, T0.AddSeconds(2));
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
        Assert.Equal(1, _sw.Switches);

        _c.Tick(false, T0.AddSeconds(4));
        _c.Tick(true, T0.AddSeconds(6));
        Assert.Equal(DisplayMode.TvAuto, _c.Mode);
    }

    [Fact]
    public void RestoreNowAlwaysGoesBackToTheDesktop()
    {
        _c.Toggle(bigPictureOpen: false);
        _c.RestoreNow(bigPictureOpen: false);
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
        Assert.False(_sw.HasSavedLayout);
    }

    [Fact]
    public void RestoreNowRestoresEvenWhenNothingWasSaved()
    {
        _c.RestoreNow(bigPictureOpen: false);
        Assert.Equal(1, _sw.Restores);
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
    }

    [Fact]
    public void RestoreNowKeepsBigPictureFromSwitchingBackRightAway()
    {
        _c.Tick(true, T0);
        _c.RestoreNow(bigPictureOpen: true);
        _c.Tick(true, T0.AddSeconds(2));
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
        Assert.Equal(1, _sw.Switches);
    }

    [Fact]
    public void StartRestoresALeftoverLayout()
    {
        _sw.HasSavedLayout = true;
        _c.Start(bigPictureOpen: false);
        Assert.Equal(1, _sw.Restores);
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
    }

    [Fact]
    public void StartResumesTvModeIfBigPictureIsStillOpen()
    {
        _sw.HasSavedLayout = true;
        _sw.TvOnly = true;
        _c.Start(bigPictureOpen: true);
        Assert.Equal(0, _sw.Restores);
        Assert.Equal(DisplayMode.TvAuto, _c.Mode);
    }

    [Fact]
    public void ShutdownNeverLeavesTheUserOnTheTv()
    {
        _c.Tick(true, T0);
        _c.Shutdown();
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
        Assert.Equal(1, _sw.Restores);

        var idle = new FakeSwitcher();
        new ModeController(idle, new ListLog()).Shutdown();
        Assert.Equal(0, idle.Restores);
    }

    [Fact]
    public void RaisesModeChanged()
    {
        var seen = new List<DisplayMode>();
        _c.ModeChanged += seen.Add;
        _c.Tick(true, T0);
        _c.Tick(false, T0.AddSeconds(1));
        _c.Tick(false, T0.AddSeconds(10));
        Assert.Equal(new[] { DisplayMode.TvAuto, DisplayMode.Desktop }, seen);
    }

    [Fact]
    public void FollowsWindowsWhenTheDesktopComesBackOnItsOwn()
    {
        bool outside = false;
        _c.LayoutChangedOutside += () => outside = true;
        _c.Tick(true, T0);
        _sw.TvOnly = false; // e.g. the TV went to standby and Windows turned the monitor back on

        _c.Tick(true, T0.AddSeconds(2));
        Assert.Equal(DisplayMode.TvAuto, _c.Mode); // one odd reading is not enough
        _c.Tick(true, T0.AddSeconds(4));
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
        Assert.True(outside);
        Assert.False(_sw.HasSavedLayout);
        Assert.Equal(0, _sw.Restores);
    }

    [Fact]
    public void AMomentaryGlitchIsIgnored()
    {
        _c.Tick(true, T0);
        _sw.TvOnly = false;
        _c.Tick(true, T0.AddSeconds(2));
        _sw.TvOnly = true;
        _c.Tick(true, T0.AddSeconds(4));
        _sw.TvOnly = false;
        _c.Tick(true, T0.AddSeconds(6));
        Assert.Equal(DisplayMode.TvAuto, _c.Mode);
    }

    [Fact]
    public void DoesNotFightWindowsWhileBigPictureStaysOpen()
    {
        _c.Tick(true, T0);
        _sw.TvOnly = false;
        _c.Tick(true, T0.AddSeconds(2));
        _c.Tick(true, T0.AddSeconds(4));
        _c.Tick(true, T0.AddSeconds(6));
        Assert.Equal(1, _sw.Switches);

        _c.Tick(false, T0.AddSeconds(8));
        _c.Tick(true, T0.AddSeconds(10));
        Assert.Equal(2, _sw.Switches);
        Assert.Equal(DisplayMode.TvAuto, _c.Mode);
    }

    [Fact]
    public void ManualModeAlsoNoticesTheDesktopCameBack()
    {
        _c.Toggle(bigPictureOpen: false);
        _sw.TvOnly = false;
        _c.Tick(false, T0);
        _c.Tick(false, T0.AddSeconds(2));
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
    }

    [Fact]
    public void SyncBeforeTheMenuChecksRightAway()
    {
        _c.Tick(true, T0);
        _sw.TvOnly = false;
        _c.SyncWithDisplays(bigPictureOpen: true);
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
    }

    [Fact]
    public void TogglingWhenTheDesktopIsAlreadyBackDoesNotSwitchToTheTv()
    {
        _c.Tick(true, T0);
        _sw.TvOnly = false;
        _c.Toggle(bigPictureOpen: true); // the user meant "back to the desktop"
        Assert.Equal(DisplayMode.Desktop, _c.Mode);
        Assert.Equal(1, _sw.Switches);
        Assert.Equal(0, _sw.Restores);
    }
}

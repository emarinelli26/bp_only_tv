using BigPictureTV.Core.Detection;
using Xunit;

namespace BigPictureTV.Core.Tests;

public class BigPictureWatcherTests
{
    static BigPictureSnapshot Snap(BigPictureWindow w, bool steamDesktop = false, bool extra = false) =>
        new(w, steamDesktop, extra);

    [Theory]
    [InlineData(BigPictureWindow.Visible, true)]
    [InlineData(BigPictureWindow.Minimized, true)]
    [InlineData(BigPictureWindow.Hidden, true)]
    [InlineData(BigPictureWindow.None, false)]
    public void Leaving_big_picture_keeps_the_tv_by_default(BigPictureWindow window, bool open) =>
        Assert.Equal(open, BigPictureWatcher.Decide(Snap(window), onlyWhileOnScreen: false));

    [Theory]
    [InlineData(BigPictureWindow.Visible, true)]
    [InlineData(BigPictureWindow.Minimized, false)]
    [InlineData(BigPictureWindow.Hidden, false)]
    public void Option_goes_to_the_desktop_when_big_picture_is_not_on_screen(BigPictureWindow window, bool open) =>
        Assert.Equal(open, BigPictureWatcher.Decide(Snap(window), onlyWhileOnScreen: true));

    [Fact]
    public void Steam_desktop_window_means_big_picture_was_left()
    {
        Assert.False(BigPictureWatcher.Decide(Snap(BigPictureWindow.Hidden, steamDesktop: true), false));
        Assert.True(BigPictureWatcher.Decide(Snap(BigPictureWindow.Visible, steamDesktop: true), false));
    }

    [Fact]
    public void Extra_program_counts_as_open()
    {
        Assert.True(BigPictureWatcher.Decide(Snap(BigPictureWindow.None, extra: true), false));
        Assert.True(BigPictureWatcher.Decide(Snap(BigPictureWindow.Hidden, extra: true), true));
    }
}

public class BigPictureTitleTests
{
    static readonly string[] Titles = { "Steam Big Picture Mode", "Steam Big Picture" };

    [Theory]
    [InlineData("Steam Big Picture Mode", null, true)]
    [InlineData("steam big picture mode", "anything", true)]
    [InlineData("Modo Big Picture de Steam", "steamwebhelper", true)]   // Steam in Spanish
    [InlineData("Steam Big Picture-Modus", "steamwebhelper", true)]     // German
    [InlineData("Mode Big Picture de Steam", "Steam", true)]            // French
    [InlineData("Modo Big Picture de Steam", null, false)]              // owner unknown
    [InlineData("Big Picture tips - YouTube", "msedge", false)]         // a web page about it
    [InlineData("Steam", "steamwebhelper", false)]                      // the normal Steam window
    [InlineData("", "steamwebhelper", false)]
    public void KnowsBigPictureInAnyLanguage(string title, string? process, bool expected) =>
        Assert.Equal(expected, BigPictureWatcher.IsBigPictureTitle(title, process, Titles));
}

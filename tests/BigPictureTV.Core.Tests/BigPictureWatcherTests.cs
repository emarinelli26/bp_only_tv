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

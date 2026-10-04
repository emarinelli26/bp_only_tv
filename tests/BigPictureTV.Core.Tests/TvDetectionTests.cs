using BigPictureTV.Core.Display;

namespace BigPictureTV.Core.Tests;

public class TvDetectionTests
{
    [Fact]
    public void ReadsManufacturerFromDevicePath()
    {
        Assert.Equal("GSM", Displays.LgTv.Manufacturer);
        Assert.Equal("HKN", Displays.Monitor.Manufacturer);
        Assert.Equal("", new DisplayInfo { DevicePath = "" }.Manufacturer);
    }

    [Fact]
    public void PicksTheLgTvOverTheMonitor()
    {
        Assert.Equal("LG TV", TvDetector.Guess(new[] { Displays.Monitor, Displays.LgTv })?.Name);
    }

    [Fact]
    public void PicksTheTvEvenWhenItIsPrimaryAndNamedOddly()
    {
        var tv = Displays.LgTv with { Name = "SAMSUNG", DevicePath = @"\\?\DISPLAY#SAM7105#x", Primary = true };
        var monitor = Displays.Monitor with { Primary = false };
        Assert.Equal("SAMSUNG", TvDetector.Guess(new[] { monitor, tv })?.Name);
    }

    [Fact]
    public void NeverPicksALaptopPanel()
    {
        var panel = new DisplayInfo { Name = "", DevicePath = @"\\?\DISPLAY#SHP14D0#x", Connection = OutputTechnology.Internal, Primary = true };
        Assert.Null(TvDetector.Guess(new[] { panel }));
        Assert.Equal("LG TV", TvDetector.Guess(new[] { panel, Displays.LgTv })?.Name);
    }

    [Fact]
    public void ReturnsNullWhenNothingLooksLikeATv()
    {
        var a = Displays.Monitor;
        var b = Displays.Monitor with { Name = "DELL U2720Q", DevicePath = @"\\?\DISPLAY#DEL4321#x", Primary = false };
        Assert.Null(TvDetector.Guess(new[] { a, b }));
        Assert.Null(TvDetector.Guess(Array.Empty<DisplayInfo>()));
    }

    [Fact]
    public void MatchesTvAsAWordOnly()
    {
        var notTv = new DisplayInfo { Name = "TVLOGIC MON", DevicePath = @"\\?\DISPLAY#ABC0001#x" };
        Assert.True(TvDetector.Score(notTv, 1) < 10);
        Assert.True(TvDetector.Score(new DisplayInfo { Name = "Hisense-TV" }, 1) >= 10);
    }

    [Fact]
    public void SettingsDevicePathWinsOverTheGuess()
    {
        var settings = new AppSettings { TvDevicePath = Displays.Monitor.DevicePath, TvName = "IQ24H" };
        var (tv, _) = TvSelector.Select(new[] { Displays.Monitor, Displays.LgTv }, settings);
        Assert.Equal("IQ24H", tv?.Name);
    }

    [Fact]
    public void SettingsFallBackToTheNameWhenThePathChanged()
    {
        var settings = new AppSettings { TvDevicePath = @"\\?\DISPLAY#GSM0001#old", TvName = "LG TV" };
        var (tv, _) = TvSelector.Select(new[] { Displays.Monitor, Displays.LgTv }, settings);
        Assert.Equal("LG TV", tv?.Name);
    }

    [Fact]
    public void ChosenTvMissingMeansNoTvRatherThanAGuess()
    {
        var settings = new AppSettings { TvDevicePath = @"\\?\DISPLAY#SNY0001#x", TvName = "SONY TV" };
        var (tv, reason) = TvSelector.Select(new[] { Displays.Monitor, Displays.LgTv }, settings);
        Assert.Null(tv);
        Assert.Contains("not connected", reason);
    }

    [Fact]
    public void CommandLineNameOverridesEverything()
    {
        var settings = new AppSettings { TvDevicePath = Displays.LgTv.DevicePath };
        Assert.Equal("IQ24H", TvSelector.Select(new[] { Displays.Monitor, Displays.LgTv }, settings, "iq24").Tv?.Name);
        Assert.Null(TvSelector.Select(new[] { Displays.Monitor, Displays.LgTv }, settings, "sony").Tv);
    }

    [Fact]
    public void NoSettingsUsesTheGuess()
    {
        var (tv, reason) = TvSelector.Select(new[] { Displays.Monitor, Displays.LgTv }, new AppSettings());
        Assert.Equal("LG TV", tv?.Name);
        Assert.Equal("detected automatically", reason);
    }
}

using BigPictureTV.Core.Audio;
using BigPictureTV.Core.Updates;

namespace BigPictureTV.Core.Tests;

public class AudioPickerTests
{
    static readonly AudioDevice Speakers = new("{0.0.0.00000000}.{a}", "Speakers (Realtek(R) Audio)");
    static readonly AudioDevice Tv = new("{0.0.0.00000000}.{b}", "LG TV (NVIDIA High Definition Audio)");
    static readonly AudioDevice[] Outputs = { Speakers, Tv };

    [Fact]
    public void FindsTheOutputNamedAfterTheTv() => Assert.Equal(Tv, AudioPicker.Pick(Outputs, "", "LG TV"));

    [Fact]
    public void ThePickedOutputWins() => Assert.Equal(Speakers, AudioPicker.Pick(Outputs, Speakers.Id, "LG TV"));

    [Fact]
    public void FallsBackWhenThePickedOutputIsGone() =>
        Assert.Equal(Tv, AudioPicker.Pick(Outputs, "{gone}", "LG TV"));

    [Fact]
    public void NothingWhenNoNameMatches() => Assert.Null(AudioPicker.Pick(Outputs, "", "Samsung"));
}

public class ReleaseVersionTests
{
    [Theory]
    [InlineData("v1.2.0", "1.2.0")]
    [InlineData("1.2", "1.2.0")]
    [InlineData("V0.7.1-beta", "0.7.1")]
    [InlineData("v2", "2.0.0")]
    public void ParsesTags(string tag, string expected) => Assert.Equal(Version.Parse(expected), ReleaseVersion.Parse(tag));

    [Theory]
    [InlineData("")]
    [InlineData("latest")]
    public void RejectsNonVersions(string tag) => Assert.Null(ReleaseVersion.Parse(tag));

    [Fact]
    public void ComparesWithTheRunningVersion()
    {
        var mine = new Version(0, 6, 0, 0);
        Assert.True(ReleaseVersion.IsNewer("v0.7.0", mine));
        Assert.False(ReleaseVersion.IsNewer("v0.6.0", mine));
        Assert.False(ReleaseVersion.IsNewer("v0.5.9", mine));
        Assert.False(ReleaseVersion.IsNewer("nonsense", mine));
    }
}

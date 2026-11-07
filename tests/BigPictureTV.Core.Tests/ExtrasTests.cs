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

    [Theory]
    [InlineData("v2.0.0", "2.0.0-beta.1", true)]      // the final version after testing it
    [InlineData("v2.0.0-beta.2", "2.0.0-beta.1", true)]
    [InlineData("v2.0.0-beta.10", "2.0.0-beta.9", true)]
    [InlineData("v2.0.0-rc.1", "2.0.0-beta.3", true)]
    [InlineData("v2.0.0-beta.1", "2.0.0", false)]     // never back to a test version
    [InlineData("v2.0.0-beta.1", "1.0.0", true)]
    [InlineData("v2.0.0", "2.0.0", false)]
    [InlineData("v2.0.0-beta.1", "2.0.0-beta.1+74d7e91", false)]
    [InlineData("v1.0.0", "2.0.0-beta.1", false)]
    public void TestVersionsComeBeforeTheFinalOne(string tag, string current, bool newer) =>
        Assert.Equal(newer, ReleaseVersion.IsNewer(tag, current));

    [Theory]
    [InlineData("2.0.0-beta.1", true)]
    [InlineData("2.0.0-beta.1+74d7e91", true)]
    [InlineData("2.0.0+74d7e91", false)]
    [InlineData("1.0.0", false)]
    public void KnowsTestVersions(string version, bool test) => Assert.Equal(test, ReleaseVersion.IsTestVersion(version));
}

public class FileLogTests
{
    [Fact]
    public void StartsAFreshFileWhenFull()
    {
        string dir = Directory.CreateTempSubdirectory("bptv-log").FullName;
        try
        {
            string file = Path.Combine(dir, "BigPictureTV.log");
            var log = new FileLog(file, console: false) { MaxBytes = 200 };
            for (int i = 0; i < 20; i++) log.Write($"line {i}");

            Assert.True(new FileInfo(file).Length < 200 + 60);
            Assert.True(File.Exists(FileLog.OldFile(file)));
            Assert.EndsWith("line 19", File.ReadAllLines(file)[^1]);
            Assert.Equal(Path.Combine(dir, "BigPictureTV.old.log"), FileLog.OldFile(file));
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

public class SettingsFileTests
{
    [Fact]
    public void ABrokenFileFallsBackToTheCopyFromBeforeTheLastSave()
    {
        string dir = Directory.CreateTempSubdirectory("bptv-settings").FullName;
        try
        {
            string file = Path.Combine(dir, "settings.json");
            new AppSettings { TvName = "LG TV" }.Save(file);
            new AppSettings { TvName = "Samsung" }.Save(file);
            Assert.True(File.Exists(SafeFile.BackupOf(file)));
            Assert.False(File.Exists(file + ".tmp"));

            File.WriteAllText(file, "{ \"TvName\": \"Sams"); // cut off mid-write

            Assert.Equal("LG TV", AppSettings.Load(file).TvName);
            Assert.True(File.Exists(file + ".broken"));
            Assert.Equal("LG TV", AppSettings.Load(file).TvName); // the file itself is good again
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Fact]
    public void DefaultsWhenNothingCanBeRead()
    {
        string dir = Directory.CreateTempSubdirectory("bptv-settings").FullName;
        try
        {
            string file = Path.Combine(dir, "settings.json");
            File.WriteAllText(file, "not json");
            Assert.Equal("", AppSettings.Load(file).TvName);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}

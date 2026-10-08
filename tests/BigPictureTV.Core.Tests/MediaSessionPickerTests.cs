using BigPictureTV.Core.TvMenu;

namespace BigPictureTV.Core.Tests;

public class MediaSessionPickerTests
{
    static MediaSessionState S(string app, bool playing) => new(app, playing);

    [Fact]
    public void NothingToShowWithoutSessions() =>
        Assert.Equal(-1, MediaSessionPicker.Pick(Array.Empty<MediaSessionState>(), -1, null));

    [Fact]
    public void PlayingMusicBeatsAPlayingVideo()
    {
        var sessions = new[] { S("MSEdge", true), S("Spotify.exe", true) };
        Assert.Equal(1, MediaSessionPicker.Pick(sessions, current: 0, last: null));
    }

    [Fact]
    public void WhatPlaysBeatsWhatIsPaused()
    {
        var sessions = new[] { S("Spotify.exe", false), S("MSEdge", true) };
        Assert.Equal(1, MediaSessionPicker.Pick(sessions, current: 0, last: null));
    }

    [Fact]
    public void StaysOnTheLastOneWhenPaused()
    {
        var sessions = new[] { S("MSEdge", false), S("Spotify.exe", false) };
        Assert.Equal(0, MediaSessionPicker.Pick(sessions, current: 1, last: "MSEdge"));
    }

    [Fact]
    public void StaysOnTheLastOneWhilePlayingEvenIfMusicStarts()
    {
        var sessions = new[] { S("MSEdge", true), S("Spotify.exe", true) };
        Assert.Equal(0, MediaSessionPicker.Pick(sessions, current: 1, last: "MSEdge"));
    }

    [Fact]
    public void WithAllPausedAndNoHistoryMusicComesFirst()
    {
        var sessions = new[] { S("MSEdge", false), S("SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", false) };
        Assert.Equal(1, MediaSessionPicker.Pick(sessions, current: 0, last: null));
    }

    [Fact]
    public void FallsBackToWhatWindowsPicked()
    {
        var sessions = new[] { S("MSEdge", false), S("Chrome", false) };
        Assert.Equal(1, MediaSessionPicker.Pick(sessions, current: 1, last: "gone"));
    }
}

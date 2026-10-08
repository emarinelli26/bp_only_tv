namespace BigPictureTV.Core.TvMenu;

/// <summary>A media session Windows knows about: the app playing it and whether it plays now.</summary>
public readonly record struct MediaSessionState(string App, bool Playing);

/// <summary>
/// Chooses which of the media sessions Windows lists the TV menu shows and
/// controls ("Now playing"): what is playing, music apps before videos, and
/// it sticks to the app it showed last so pausing it doesn't make it vanish.
/// </summary>
public static class MediaSessionPicker
{
    static readonly string[] MusicApps = { "spotify", "music", "tidal", "deezer", "foobar", "aimp", "musicbee", "winamp", "vlc" };

    public static bool IsMusicApp(string app) =>
        MusicApps.Any(m => app.Contains(m, StringComparison.OrdinalIgnoreCase));

    /// <param name="current">The session Windows itself calls current, or -1.</param>
    /// <param name="last">App of the session shown last, or null.</param>
    /// <returns>Index of the session to show, or -1 if there is none.</returns>
    public static int Pick(IReadOnlyList<MediaSessionState> sessions, int current, string? last)
    {
        if (sessions.Count == 0) return -1;
        int lastIndex = last == null ? -1 : IndexOf(sessions, s => s.App == last);

        // Something plays: the one shown before, else a music app, else what Windows picked.
        if (lastIndex >= 0 && sessions[lastIndex].Playing) return lastIndex;
        int music = IndexOf(sessions, s => s.Playing && IsMusicApp(s.App));
        if (music >= 0) return music;
        if (current >= 0 && current < sessions.Count && sessions[current].Playing) return current;
        int playing = IndexOf(sessions, s => s.Playing);
        if (playing >= 0) return playing;

        // All paused: keep the one shown, so Start can resume it.
        if (lastIndex >= 0) return lastIndex;
        music = IndexOf(sessions, s => IsMusicApp(s.App));
        if (music >= 0) return music;
        return current >= 0 && current < sessions.Count ? current : 0;
    }

    static int IndexOf(IReadOnlyList<MediaSessionState> sessions, Func<MediaSessionState, bool> match)
    {
        for (int i = 0; i < sessions.Count; i++)
            if (match(sessions[i])) return i;
        return -1;
    }
}

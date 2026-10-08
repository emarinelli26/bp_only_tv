using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BigPictureTV.Core;
using BigPictureTV.Core.TvMenu;
using Windows.Media.Control;
using Windows.Storage.Streams;

namespace BigPictureTV.App;

/// <summary>What the TV menu shows as "Now playing".</summary>
sealed record NowPlayingInfo(string Title, string Artist, string App, bool Playing, byte[]? Cover);

/// <summary>
/// What is playing on the PC (Spotify, a web player, any app that shows in
/// Windows' own media controls) and its play, pause and skip buttons.
/// </summary>
sealed class NowPlaying
{
    const uint MaxCover = 4 * 1024 * 1024;

    readonly ILog _log;
    GlobalSystemMediaTransportControlsSessionManager? _manager;
    GlobalSystemMediaTransportControlsSession? _shown;
    string? _lastApp;
    string _coverOf = "";
    byte[]? _cover;
    bool _unavailable;

    public NowPlaying(ILog log) => _log = log;

    /// <summary>What to show now, or null if nothing plays or Windows can't tell.</summary>
    public async Task<NowPlayingInfo?> ReadAsync()
    {
        if (_unavailable) return null;
        try
        {
            _manager ??= await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
            var sessions = _manager.GetSessions().ToList();
            string? currentApp = _manager.GetCurrentSession()?.SourceAppUserModelId;
            var states = sessions.Select(s => new MediaSessionState(s.SourceAppUserModelId,
                s.GetPlaybackInfo()?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing)).ToList();
            int pick = MediaSessionPicker.Pick(states, states.FindIndex(s => s.App == currentApp), _lastApp);
            if (pick < 0)
            {
                _shown = null;
                return null;
            }
            var session = sessions[pick];
            _shown = session;
            _lastApp = states[pick].App;
            var props = await session.TryGetMediaPropertiesAsync();
            string title = props?.Title ?? "", artist = props?.Artist ?? "";
            string coverOf = $"{_lastApp}|{title}|{artist}";
            if (coverOf != _coverOf)
            {
                _coverOf = coverOf;
                _cover = await ReadCoverAsync(props?.Thumbnail);
            }
            return new NowPlayingInfo(title, artist, AppName(_lastApp), states[pick].Playing, _cover);
        }
        catch (Exception e)
        {
            // Older Windows without these controls, or a session that went away mid-read.
            if (e is TypeLoadException or PlatformNotSupportedException or NotSupportedException)
            {
                _unavailable = true;
                _log.Write($"Windows can't tell what is playing: {e.Message}");
            }
            return null;
        }
    }

    /// <summary>LB/RB skip, Start (or A on the strip) plays or pauses, on what the menu shows.</summary>
    public async Task SendAsync(PadAction action)
    {
        var session = _shown;
        if (session == null) return;
        try
        {
            bool done = action switch
            {
                PadAction.Previous => await session.TrySkipPreviousAsync(),
                PadAction.Next => await session.TrySkipNextAsync(),
                PadAction.PlayPause or PadAction.Accept => await session.TryTogglePlayPauseAsync(),
                _ => true,
            };
            if (!done) _log.Write($"{AppName(_lastApp)} ignored {action}.");
        }
        catch (Exception e)
        {
            _log.Write($"Media button failed: {e.Message}");
        }
    }

    static async Task<byte[]?> ReadCoverAsync(IRandomAccessStreamReference? reference)
    {
        if (reference == null) return null;
        try
        {
            using var stream = await reference.OpenReadAsync();
            if (stream.Size == 0 || stream.Size > MaxCover) return null;
            using var reader = new DataReader(stream);
            uint size = (uint)stream.Size;
            await reader.LoadAsync(size);
            var bytes = new byte[size];
            reader.ReadBytes(bytes);
            return bytes;
        }
        catch (Exception)
        {
            return null;
        }
    }

    // "Spotify.exe", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify", "MSEdge"...
    static string AppName(string? app)
    {
        if (string.IsNullOrEmpty(app)) return "";
        if (app.Contains("spotify", StringComparison.OrdinalIgnoreCase)) return "Spotify";
        // A web player: the browser's name says nothing about the music.
        if (app.Contains("msedge", StringComparison.OrdinalIgnoreCase) || app.Contains("chrome", StringComparison.OrdinalIgnoreCase)) return "";
        string name = app[(app.LastIndexOf('!') + 1)..];
        name = name[(name.LastIndexOf('\\') + 1)..];
        return name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? name[..^4] : name;
    }
}

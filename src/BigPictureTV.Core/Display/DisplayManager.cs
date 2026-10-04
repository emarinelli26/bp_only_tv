namespace BigPictureTV.Core.Display;

/// <summary>The two display actions the rest of the app needs.</summary>
public interface IDisplaySwitcher
{
    /// <summary>True if a desktop layout is saved, i.e. we are (or were left) on the TV.</summary>
    bool HasSavedLayout { get; }

    /// <summary>Saves the desktop layout and leaves only the TV on. False if it did not happen.</summary>
    bool SwitchToTv();

    /// <summary>Puts the saved desktop layout back, falling back to Extend.</summary>
    void RestoreDesktop();
}

/// <summary>Switches between the desktop layout and TV only. Ported from BigPictureTV.ps1.</summary>
public sealed class DisplayManager : IDisplaySwitcher
{
    readonly IDisplayConfig _display;
    readonly LayoutStore _store;
    readonly Func<IReadOnlyList<DisplayInfo>, (DisplayInfo? Tv, string Reason)> _selectTv;
    readonly ILog _log;

    public DisplayManager(IDisplayConfig display, LayoutStore store,
        Func<IReadOnlyList<DisplayInfo>, (DisplayInfo? Tv, string Reason)> selectTv, ILog log)
    {
        _display = display;
        _store = store;
        _selectTv = selectTv;
        _log = log;
    }

    public bool HasSavedLayout => _store.HasSaved;

    public bool SwitchToTv()
    {
        var (tv, reason) = _selectTv(_display.ListDisplays());
        if (tv == null)
        {
            _log.Write($"Can't switch to the TV: {reason}.");
            return false;
        }

        var layout = _display.BuildSingleDisplay(tv.DevicePath);
        if (layout == null)
        {
            _log.Write($"Can't switch to the TV: {tv} is not connected.");
            return false;
        }

        var current = _display.QueryActive();
        if (current.Paths.Length == 1 && layout.Modes.Length > 0 &&
            current.Paths[0].targetInfo.id == layout.Paths[0].targetInfo.id &&
            current.Paths[0].targetInfo.adapterId.LowPart == layout.Paths[0].targetInfo.adapterId.LowPart &&
            current.Paths[0].targetInfo.adapterId.HighPart == layout.Paths[0].targetInfo.adapterId.HighPart)
        {
            _log.Write("The TV is already the only display.");
            return true;
        }

        // Only save if nothing is saved yet, so a second switch never
        // overwrites the real desktop layout with the TV-only one.
        if (!_store.HasSaved)
        {
            _store.Save(current);
            _log.Write($"Saved current layout ({current.Paths.Length} active display(s)).");
        }

        int err = _display.Apply(layout, saveToDatabase: false);
        if (err != 0)
        {
            _log.Write($"Switching to the TV failed (error {err}).");
            return false;
        }
        _log.Write($"Switched to TV only: {tv} ({reason}).");
        return true;
    }

    public void RestoreDesktop()
    {
        if (_store.HasSaved)
        {
            try
            {
                int err = _display.Apply(_store.Load(), saveToDatabase: true);
                if (err == 0)
                {
                    _store.Delete();
                    _log.Write("Restored saved layout.");
                    return;
                }
                _log.Write($"Saved layout could not be applied (error {err}), falling back to Extend.");
            }
            catch (Exception e) when (e is FormatException or IOException or System.Text.Json.JsonException)
            {
                _log.Write($"Saved layout unreadable ({e.Message}), falling back to Extend.");
            }
        }

        // Adapter ids change after a reboot or driver update, which invalidates
        // a saved layout. Windows still remembers the last extended arrangement.
        int extendErr = _display.ApplyExtend();
        if (extendErr == 0)
        {
            _store.Delete();
            _log.Write("Restored the last extended layout.");
        }
        else
        {
            _log.Write($"Restoring the extended layout failed (error {extendErr}).");
        }
    }
}

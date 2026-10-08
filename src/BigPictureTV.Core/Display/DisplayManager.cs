namespace BigPictureTV.Core.Display;

/// <summary>The two display actions the rest of the app needs.</summary>
public interface IDisplaySwitcher
{
    /// <summary>True if a desktop layout is saved, i.e. we are (or were left) on the TV.</summary>
    bool HasSavedLayout { get; }

    /// <summary>Saves the desktop layout and switches to the TV layout. False if it did not happen.</summary>
    bool SwitchToTv();

    /// <summary>Puts the saved desktop layout back, falling back to Extend.</summary>
    void RestoreDesktop();

    /// <summary>True if, right now, the displays show the TV layout we switched to.</summary>
    bool IsOnTv();

    /// <summary>True if the TV we switched to is still on, whatever else came on with it.</summary>
    bool TvIsActive();

    /// <summary>Drops the saved layout without applying it (the desktop is already back).</summary>
    void ForgetSavedLayout();
}

/// <summary>Switches between the desktop layout and TV only. Ported from BigPictureTV.ps1.</summary>
public sealed class DisplayManager : IDisplaySwitcher
{
    readonly IDisplayConfig _display;
    readonly LayoutStore _store;
    readonly Func<IReadOnlyList<DisplayInfo>, (DisplayInfo? Tv, string Reason)> _selectTv;
    readonly ILog _log;

    // Device path of the display we switched to, so changing the chosen TV
    // in the settings while on the TV doesn't look like the layout changed.
    string? _switchedTo;
    TvLayout? _switchedLayout;
    readonly Func<TvLayout> _layout;

    public DisplayManager(IDisplayConfig display, LayoutStore store,
        Func<IReadOnlyList<DisplayInfo>, (DisplayInfo? Tv, string Reason)> selectTv, ILog log,
        Func<TvLayout>? layout = null)
    {
        _layout = layout ?? (() => TvLayout.TvOnly);
        _display = display;
        _store = store;
        _selectTv = selectTv;
        _log = log;
    }

    public bool HasSavedLayout => _store.HasSaved;

    public bool SwitchToTv()
    {
        var displays = _display.ListDisplays();
        var (tv, reason) = _selectTv(displays);
        if (tv == null)
        {
            _log.Write($"Can't switch to the TV: {reason}.");
            return false;
        }

        var layout = _layout();
        if (IsIn(layout, displays, tv.DevicePath))
        {
            _log.Write($"Already on the TV ({layout}).");
            Remember(tv, layout);
            return true;
        }

        Layout? target = null;
        if (layout == TvLayout.TvOnly)
        {
            target = _display.BuildSingleDisplay(tv.DevicePath);
            if (target == null)
            {
                _log.Write($"Can't switch to the TV: {tv} is not connected.");
                return false;
            }
        }

        // Only save if nothing is saved yet, so a second switch never
        // overwrites the real desktop layout with a TV one.
        if (!_store.HasSaved)
        {
            var current = _display.QueryActive();
            _store.Save(current);
            _log.Write($"Saved current layout ({current.Paths.Length} active display(s)).");
        }

        int err = layout switch
        {
            TvLayout.TvPrimary => ApplyPrimary(tv),
            TvLayout.Duplicate => _display.ApplyClone(),
            _ => _display.Apply(target!, saveToDatabase: false),
        };
        if (err != 0)
        {
            _log.Write($"Switching to the TV failed (error {err}).");
            return false;
        }
        _log.Write($"Switched to the TV ({layout}): {tv} ({reason}).");
        Remember(tv, layout);
        return true;
    }

    int ApplyPrimary(DisplayInfo tv)
    {
        var target = _display.BuildPrimary(tv.DevicePath);
        if (target == null)
        {
            // The TV is connected but switched off in Windows: extend first so it comes on.
            int err = _display.ApplyExtend();
            if (err != 0) return err;
            target = _display.BuildPrimary(tv.DevicePath);
            if (target == null)
            {
                _log.Write($"{tv} did not turn on after extending the desktop.");
                return -1;
            }
        }
        return _display.Apply(target, saveToDatabase: false);
    }

    void Remember(DisplayInfo tv, TvLayout layout)
    {
        _switchedTo = tv.DevicePath;
        _switchedLayout = layout;
    }

    public bool IsOnTv()
    {
        var displays = _display.ListDisplays();
        string? tvPath = _switchedTo ?? _selectTv(displays).Tv?.DevicePath;
        return tvPath != null && IsIn(_switchedLayout ?? _layout(), displays, tvPath);
    }

    public bool TvIsActive()
    {
        var displays = _display.ListDisplays();
        string? tvPath = _switchedTo ?? _selectTv(displays).Tv?.DevicePath;
        return tvPath != null && displays.Any(d => d.Active && string.Equals(d.DevicePath, tvPath, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Whether the displays already show the given TV layout.</summary>
    public static bool IsIn(TvLayout layout, IReadOnlyList<DisplayInfo> displays, string tvPath)
    {
        var active = displays.Where(d => d.Active).ToList();
        var tv = active.FirstOrDefault(d => string.Equals(d.DevicePath, tvPath, StringComparison.OrdinalIgnoreCase));
        if (tv == null) return false;
        return layout switch
        {
            TvLayout.TvPrimary => tv.Primary,
            TvLayout.Duplicate => active.All(d => d.Primary), // cloned displays share one source at (0,0)
            _ => active.Count == 1,
        };
    }

    public void ForgetSavedLayout()
    {
        _store.Delete();
        _switchedTo = null;
        _switchedLayout = null;
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
                    ForgetSavedLayout();
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
            ForgetSavedLayout();
            _log.Write("Restored the last extended layout.");
        }
        else
        {
            _log.Write($"Restoring the extended layout failed (error {extendErr}).");
        }
    }
}

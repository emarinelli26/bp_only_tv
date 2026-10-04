using System.Runtime.InteropServices;
using static BigPictureTV.Core.Display.NativeMethods;

namespace BigPictureTV.Core.Display;

/// <summary>Reads and changes the Windows display layout.</summary>
public interface IDisplayConfig
{
    /// <summary>One entry per connected display, active or not.</summary>
    IReadOnlyList<DisplayInfo> ListDisplays();

    /// <summary>The layout as it is right now (active displays only).</summary>
    Layout QueryActive();

    /// <summary>
    /// A layout with only the given display active, at (0,0) so it is primary.
    /// Null if that display is not connected.
    /// </summary>
    Layout? BuildSingleDisplay(string devicePath);

    /// <summary>Applies a layout. Returns the Win32 error code (0 = success).</summary>
    int Apply(Layout layout, bool saveToDatabase);

    /// <summary>Applies Windows' last remembered Extend arrangement.</summary>
    int ApplyExtend();
}

/// <summary>The real implementation, on top of the CCD API. Windows only.</summary>
public sealed class DisplayConfig : IDisplayConfig
{
    public IReadOnlyList<DisplayInfo> ListDisplays()
    {
        // QDC_ALL_PATHS lists a path for every source/target combination, so
        // targets repeat; keep one entry per target, preferring the active path.
        var all = Query(QDC_ALL_PATHS);
        var order = new List<string>();
        var byTarget = new Dictionary<string, DisplayInfo>();
        foreach (var p in all.Paths)
        {
            var t = p.targetInfo;
            if (t.targetAvailable == 0) continue;
            string key = $"{t.adapterId.LowPart}:{t.adapterId.HighPart}:{t.id}";
            bool active = (p.flags & PATH_ACTIVE) != 0;
            if (byTarget.TryGetValue(key, out var existing) && (existing.Active || !active)) continue;

            var name = GetTargetName(t.adapterId, t.id);
            uint width = 0, height = 0;
            bool primary = false;
            if (active && p.sourceInfo.modeInfoIdx != INVALID_IDX && p.sourceInfo.modeInfoIdx < all.Modes.Length)
            {
                var src = all.Modes[p.sourceInfo.modeInfoIdx];
                width = src.sourceWidth;
                height = src.sourceHeight;
                primary = src.sourcePositionX == 0 && src.sourcePositionY == 0;
            }
            if (!byTarget.ContainsKey(key)) order.Add(key);
            byTarget[key] = new DisplayInfo
            {
                Name = name.monitorFriendlyDeviceName ?? "",
                DevicePath = name.monitorDevicePath ?? "",
                Active = active,
                Primary = primary,
                Connection = (OutputTechnology)t.outputTechnology,
                Width = width,
                Height = height,
            };
        }
        return order.Select(k => byTarget[k]).ToList();
    }

    public Layout QueryActive() => Query(QDC_ONLY_ACTIVE_PATHS);

    public Layout? BuildSingleDisplay(string devicePath)
    {
        var active = Query(QDC_ONLY_ACTIVE_PATHS);
        foreach (var p in active.Paths)
        {
            if (!PathIs(p, devicePath)) continue;

            var path = p;
            var modes = new List<MODE_INFO>();
            if (path.sourceInfo.modeInfoIdx != INVALID_IDX)
            {
                var src = active.Modes[path.sourceInfo.modeInfoIdx];
                src.sourcePositionX = 0;
                src.sourcePositionY = 0;
                path.sourceInfo.modeInfoIdx = (uint)modes.Count;
                modes.Add(src);
            }
            if (path.targetInfo.modeInfoIdx != INVALID_IDX)
            {
                path.targetInfo.modeInfoIdx = (uint)modes.Count;
                modes.Add(active.Modes[p.targetInfo.modeInfoIdx]);
            }
            return new Layout { Paths = new[] { path }, Modes = modes.ToArray() };
        }

        // Connected but currently switched off in Windows: activate it and let
        // Windows pick its mode.
        var all = Query(QDC_ALL_PATHS);
        foreach (var p in all.Paths)
        {
            if (p.targetInfo.targetAvailable == 0 || !PathIs(p, devicePath)) continue;
            var path = p;
            path.flags = PATH_ACTIVE;
            path.sourceInfo.modeInfoIdx = INVALID_IDX;
            path.targetInfo.modeInfoIdx = INVALID_IDX;
            return new Layout { Paths = new[] { path } };
        }
        return null;
    }

    public int Apply(Layout layout, bool saveToDatabase)
    {
        uint flags = ApplyFlags | (saveToDatabase ? SDC_SAVE_TO_DATABASE : 0);
        return SetDisplayConfig((uint)layout.Paths.Length, layout.Paths,
            (uint)layout.Modes.Length, layout.Modes.Length == 0 ? null : layout.Modes, flags);
    }

    public int ApplyExtend() => SetDisplayConfig(0, null, 0, null, SDC_APPLY | SDC_TOPOLOGY_EXTEND);

    static bool PathIs(PATH_INFO p, string devicePath) =>
        string.Equals(GetTargetName(p.targetInfo.adapterId, p.targetInfo.id).monitorDevicePath,
            devicePath, StringComparison.OrdinalIgnoreCase);

    static Layout Query(uint flags)
    {
        const int ERROR_INSUFFICIENT_BUFFER = 122;
        for (int attempt = 0; attempt < 5; attempt++)
        {
            int err = GetDisplayConfigBufferSizes(flags, out uint numPaths, out uint numModes);
            if (err != 0) throw new InvalidOperationException("GetDisplayConfigBufferSizes failed: " + err);
            var paths = new PATH_INFO[numPaths];
            var modes = new MODE_INFO[numModes];
            err = QueryDisplayConfig(flags, ref numPaths, paths, ref numModes, modes, IntPtr.Zero);
            if (err == ERROR_INSUFFICIENT_BUFFER) continue; // layout changed between calls
            if (err != 0) throw new InvalidOperationException("QueryDisplayConfig failed: " + err);
            Array.Resize(ref paths, (int)numPaths);
            Array.Resize(ref modes, (int)numModes);
            return new Layout { Paths = paths, Modes = modes };
        }
        throw new InvalidOperationException("QueryDisplayConfig kept changing size");
    }

    static TARGET_DEVICE_NAME GetTargetName(LUID adapterId, uint targetId)
    {
        var req = new TARGET_DEVICE_NAME
        {
            type = GET_TARGET_NAME,
            size = (uint)Marshal.SizeOf<TARGET_DEVICE_NAME>(),
            adapterId = adapterId,
            id = targetId,
        };
        if (DisplayConfigGetDeviceInfo(ref req) != 0)
        {
            req.monitorFriendlyDeviceName = "";
            req.monitorDevicePath = "";
        }
        return req;
    }
}

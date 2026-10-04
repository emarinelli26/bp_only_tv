<#
.SYNOPSIS
    Keeps Steam Big Picture (and anything launched from it) on the TV only.

.DESCRIPTION
    Watches for the Steam Big Picture window. When it appears, every display
    except the TV is turned off and the TV becomes the primary display. When
    Big Picture closes, the previous display layout is restored.

    Uses the Windows CCD API (QueryDisplayConfig / SetDisplayConfig), the same
    API the Settings > Display page uses. No admin rights are needed.

.PARAMETER TvName
    Part of the TV's monitor name as Windows reports it (case-insensitive),
    for example "LG TV" or "SAMSUNG". Run with -ListDisplays to see the names.

.PARAMETER ListDisplays
    Print the connected displays and exit.

.PARAMETER Restore
    Restore the last saved layout and exit. Use this if a crash ever leaves
    you stuck on the TV only.

.PARAMETER TvOnly
    Switch to the TV only right now and exit (saves the current layout first).

.PARAMETER ExtraProcesses
    Process names (without .exe) that should also keep the TV-only layout
    while they run, e.g. "retroarch","dolphin".

.PARAMETER PollSeconds
    How often to check for Big Picture. Default 2.

.PARAMETER GraceSeconds
    How long Big Picture must stay closed before the layout is restored.
    Prevents flicker when Steam briefly recreates its window. Default 5.

.EXAMPLE
    .\BigPictureTV.ps1 -ListDisplays
.EXAMPLE
    .\BigPictureTV.ps1 -TvName "LG TV"
.EXAMPLE
    .\BigPictureTV.ps1 -Restore
#>
[CmdletBinding(DefaultParameterSetName = 'Watch')]
param(
    [Parameter(ParameterSetName = 'Watch', Mandatory = $true, Position = 0)]
    [Parameter(ParameterSetName = 'TvOnly', Mandatory = $true, Position = 0)]
    [string]$TvName,

    [Parameter(ParameterSetName = 'List', Mandatory = $true)]
    [switch]$ListDisplays,

    [Parameter(ParameterSetName = 'Restore', Mandatory = $true)]
    [switch]$Restore,

    [Parameter(ParameterSetName = 'TvOnly', Mandatory = $true)]
    [switch]$TvOnly,

    [Parameter(ParameterSetName = 'Watch')]
    [string[]]$ExtraProcesses = @(),

    [Parameter(ParameterSetName = 'Watch')]
    [ValidateRange(1, 60)]
    [int]$PollSeconds = 2,

    [Parameter(ParameterSetName = 'Watch')]
    [ValidateRange(0, 600)]
    [int]$GraceSeconds = 5
)

$ErrorActionPreference = 'Stop'

# powershell -File passes "a,b" as one string, so accept comma-separated names too.
$ExtraProcesses = @($ExtraProcesses | ForEach-Object { $_ -split ',' } |
    ForEach-Object { $_.Trim() -replace '\.exe$', '' } | Where-Object { $_ })

$DataDir    = Join-Path $env:LOCALAPPDATA 'BigPictureTV'
$LayoutFile = Join-Path $DataDir 'saved-layout.json'
$LogFile    = Join-Path $DataDir 'BigPictureTV.log'
$BigPictureTitles = @('Steam Big Picture Mode', 'Steam Big Picture')

if (-not (Test-Path $DataDir)) { New-Item -ItemType Directory -Path $DataDir | Out-Null }

function Write-Log([string]$Message) {
    $line = '{0:yyyy-MM-dd HH:mm:ss}  {1}' -f (Get-Date), $Message
    Write-Host $line
    try { Add-Content -Path $LogFile -Value $line } catch { }
}

if (-not ('BigPictureTV.Native' -as [type])) {
Add-Type -TypeDefinition @'
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;

namespace BigPictureTV
{
    [StructLayout(LayoutKind.Sequential)]
    public struct LUID { public uint LowPart; public int HighPart; }

    [StructLayout(LayoutKind.Sequential)]
    public struct RATIONAL { public uint Numerator; public uint Denominator; }

    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_SOURCE_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_TARGET_INFO
    {
        public LUID adapterId;
        public uint id;
        public uint modeInfoIdx;
        public uint outputTechnology;
        public uint rotation;
        public uint scaling;
        public RATIONAL refreshRate;
        public uint scanLineOrdering;
        public int targetAvailable;
        public uint statusFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct PATH_INFO
    {
        public PATH_SOURCE_INFO sourceInfo;
        public PATH_TARGET_INFO targetInfo;
        public uint flags;
    }

    // DISPLAYCONFIG_MODE_INFO is 64 bytes: a 16-byte header and a 48-byte union.
    // The raw words keep every byte of the union intact; the named fields
    // overlay the source-mode variant, which is the only one we edit.
    [StructLayout(LayoutKind.Explicit, Size = 64)]
    public struct MODE_INFO
    {
        [FieldOffset(0)]  public uint infoType;
        [FieldOffset(4)]  public uint id;
        [FieldOffset(8)]  public LUID adapterId;
        [FieldOffset(16)] public ulong raw0;
        [FieldOffset(24)] public ulong raw1;
        [FieldOffset(32)] public ulong raw2;
        [FieldOffset(40)] public ulong raw3;
        [FieldOffset(48)] public ulong raw4;
        [FieldOffset(56)] public ulong raw5;
        [FieldOffset(16)] public uint sourceWidth;
        [FieldOffset(20)] public uint sourceHeight;
        [FieldOffset(24)] public uint sourcePixelFormat;
        [FieldOffset(28)] public int  sourcePositionX;
        [FieldOffset(32)] public int  sourcePositionY;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TARGET_DEVICE_NAME
    {
        public uint type;
        public uint size;
        public LUID adapterId;
        public uint id;
        public uint flags;
        public uint outputTechnology;
        public ushort edidManufactureId;
        public ushort edidProductCodeId;
        public uint connectorInstance;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string monitorFriendlyDeviceName;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
        public string monitorDevicePath;
    }

    public class DisplayInfo
    {
        public string Name;
        public string DevicePath;
        public bool Active;
        public bool Available;
        public int PathIndex;
    }

    public class Layout
    {
        public PATH_INFO[] Paths;
        public MODE_INFO[] Modes;
    }

    public static class Native
    {
        public const uint QDC_ALL_PATHS = 0x1;
        public const uint QDC_ONLY_ACTIVE_PATHS = 0x2;
        public const uint SDC_TOPOLOGY_EXTEND = 0x4;
        public const uint SDC_USE_SUPPLIED_DISPLAY_CONFIG = 0x20;
        public const uint SDC_APPLY = 0x80;
        public const uint SDC_SAVE_TO_DATABASE = 0x200;
        public const uint SDC_ALLOW_CHANGES = 0x400;
        public const uint PATH_ACTIVE = 0x1;
        public const uint MODE_TYPE_SOURCE = 1;
        public const uint MODE_TYPE_TARGET = 2;
        public const uint INVALID_IDX = 0xFFFFFFFF;
        const uint GET_TARGET_NAME = 2;

        [DllImport("user32.dll")]
        static extern int GetDisplayConfigBufferSizes(uint flags, out uint numPaths, out uint numModes);

        [DllImport("user32.dll")]
        static extern int QueryDisplayConfig(uint flags, ref uint numPaths, [Out] PATH_INFO[] paths,
            ref uint numModes, [Out] MODE_INFO[] modes, IntPtr topologyId);

        [DllImport("user32.dll")]
        static extern int SetDisplayConfig(uint numPaths, [In] PATH_INFO[] paths,
            uint numModes, [In] MODE_INFO[] modes, uint flags);

        [DllImport("user32.dll")]
        static extern int DisplayConfigGetDeviceInfo(ref TARGET_DEVICE_NAME request);

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc callback, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int maxCount);

        public static Layout Query(uint flags)
        {
            const int ERROR_INSUFFICIENT_BUFFER = 122;
            for (int attempt = 0; attempt < 5; attempt++)
            {
                uint numPaths, numModes;
                int err = GetDisplayConfigBufferSizes(flags, out numPaths, out numModes);
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

        public static int Apply(Layout layout, uint flags)
        {
            return SetDisplayConfig((uint)layout.Paths.Length, layout.Paths,
                (uint)layout.Modes.Length, layout.Modes.Length == 0 ? null : layout.Modes, flags);
        }

        public static int ApplyExtendTopology()
        {
            return SetDisplayConfig(0, null, 0, null, SDC_APPLY | SDC_TOPOLOGY_EXTEND);
        }

        public static TARGET_DEVICE_NAME GetTargetName(LUID adapterId, uint targetId)
        {
            var req = new TARGET_DEVICE_NAME();
            req.type = GET_TARGET_NAME;
            req.size = (uint)Marshal.SizeOf(typeof(TARGET_DEVICE_NAME));
            req.adapterId = adapterId;
            req.id = targetId;
            int err = DisplayConfigGetDeviceInfo(ref req);
            if (err != 0) { req.monitorFriendlyDeviceName = ""; req.monitorDevicePath = ""; }
            return req;
        }

        // One entry per physical monitor (QDC_ALL_PATHS lists a path for every
        // source/target combination, so targets repeat).
        public static List<DisplayInfo> ListDisplays()
        {
            var all = Query(QDC_ALL_PATHS);
            var result = new List<DisplayInfo>();
            var seen = new Dictionary<string, DisplayInfo>();
            for (int i = 0; i < all.Paths.Length; i++)
            {
                var t = all.Paths[i].targetInfo;
                string key = t.adapterId.LowPart + ":" + t.adapterId.HighPart + ":" + t.id;
                bool active = (all.Paths[i].flags & PATH_ACTIVE) != 0;
                DisplayInfo info;
                if (seen.TryGetValue(key, out info))
                {
                    if (active && !info.Active) { info.Active = true; info.PathIndex = i; }
                    continue;
                }
                if (t.targetAvailable == 0) continue;
                var name = GetTargetName(t.adapterId, t.id);
                info = new DisplayInfo {
                    Name = name.monitorFriendlyDeviceName,
                    DevicePath = name.monitorDevicePath,
                    Active = active,
                    Available = true,
                    PathIndex = i
                };
                seen[key] = info;
                result.Add(info);
            }
            return result;
        }

        static bool NameMatches(LUID adapter, uint targetId, string tvName)
        {
            var n = GetTargetName(adapter, targetId).monitorFriendlyDeviceName ?? "";
            return n.IndexOf(tvName, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        // Builds a layout with only the TV active, positioned at (0,0) so it is
        // the primary display. Returns null if the TV is not connected.
        public static Layout BuildTvOnly(string tvName)
        {
            var active = Query(QDC_ONLY_ACTIVE_PATHS);
            foreach (var p in active.Paths)
            {
                if (!NameMatches(p.targetInfo.adapterId, p.targetInfo.id, tvName)) continue;

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

            // TV is connected but currently switched off in Windows: activate it
            // and let Windows pick its mode.
            var all = Query(QDC_ALL_PATHS);
            foreach (var p in all.Paths)
            {
                if (p.targetInfo.targetAvailable == 0) continue;
                if (!NameMatches(p.targetInfo.adapterId, p.targetInfo.id, tvName)) continue;
                var path = p;
                path.flags = PATH_ACTIVE;
                path.sourceInfo.modeInfoIdx = INVALID_IDX;
                path.targetInfo.modeInfoIdx = INVALID_IDX;
                return new Layout { Paths = new[] { path }, Modes = new MODE_INFO[0] };
            }
            return null;
        }

        // Raw struct bytes as base64, so a layout can be saved to disk and
        // reapplied byte-for-byte.
        public static string ToBase64<T>(T[] items) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            var bytes = new byte[size * items.Length];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < items.Length; i++)
                {
                    Marshal.StructureToPtr(items[i], ptr, false);
                    Marshal.Copy(ptr, bytes, i * size, size);
                }
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return Convert.ToBase64String(bytes);
        }

        public static T[] FromBase64<T>(string text) where T : struct
        {
            int size = Marshal.SizeOf(typeof(T));
            var bytes = Convert.FromBase64String(text ?? "");
            if (bytes.Length % size != 0) throw new FormatException("Saved layout has the wrong size");
            var items = new T[bytes.Length / size];
            IntPtr ptr = Marshal.AllocHGlobal(size);
            try
            {
                for (int i = 0; i < items.Length; i++)
                {
                    Marshal.Copy(bytes, i * size, ptr, size);
                    items[i] = (T)Marshal.PtrToStructure(ptr, typeof(T));
                }
            }
            finally { Marshal.FreeHGlobal(ptr); }
            return items;
        }

        public static string PathsToBase64(PATH_INFO[] p) { return ToBase64(p); }
        public static string ModesToBase64(MODE_INFO[] m) { return ToBase64(m); }
        public static PATH_INFO[] PathsFromBase64(string s) { return FromBase64<PATH_INFO>(s); }
        public static MODE_INFO[] ModesFromBase64(string s) { return FromBase64<MODE_INFO>(s); }

        public static bool AnyWindowTitled(string[] titles)
        {
            bool found = false;
            var sb = new StringBuilder(256);
            EnumWindows((hWnd, lParam) =>
            {
                if (!IsWindowVisible(hWnd)) return true;
                sb.Length = 0;
                if (GetWindowText(hWnd, sb, sb.Capacity) == 0) return true;
                string title = sb.ToString();
                foreach (var t in titles)
                {
                    if (string.Equals(title, t, StringComparison.OrdinalIgnoreCase)) { found = true; return false; }
                }
                return true;
            }, IntPtr.Zero);
            return found;
        }
    }
}
'@
}

$ApplyFlags = [BigPictureTV.Native]::SDC_APPLY -bor
              [BigPictureTV.Native]::SDC_USE_SUPPLIED_DISPLAY_CONFIG -bor
              [BigPictureTV.Native]::SDC_ALLOW_CHANGES

function Save-Layout {
    $layout = [BigPictureTV.Native]::Query([BigPictureTV.Native]::QDC_ONLY_ACTIVE_PATHS)
    [pscustomobject]@{
        Saved = (Get-Date).ToString('o')
        Paths = [BigPictureTV.Native]::PathsToBase64($layout.Paths)
        Modes = [BigPictureTV.Native]::ModesToBase64($layout.Modes)
    } | ConvertTo-Json | Set-Content -Path $LayoutFile -Encoding UTF8
    Write-Log "Saved current layout ($($layout.Paths.Count) active display(s))."
}

function Restore-Layout {
    if (Test-Path $LayoutFile) {
        try {
            $saved = Get-Content -Raw -Path $LayoutFile | ConvertFrom-Json
            $layout = New-Object BigPictureTV.Layout
            $layout.Paths = [BigPictureTV.Native]::PathsFromBase64($saved.Paths)
            $layout.Modes = [BigPictureTV.Native]::ModesFromBase64($saved.Modes)
            $err = [BigPictureTV.Native]::Apply($layout, $ApplyFlags -bor [BigPictureTV.Native]::SDC_SAVE_TO_DATABASE)
            if ($err -eq 0) {
                Remove-Item $LayoutFile -ErrorAction SilentlyContinue
                Write-Log 'Restored saved layout.'
                return
            }
            Write-Log "Saved layout could not be applied (error $err), falling back to Extend."
        } catch {
            Write-Log "Saved layout unreadable ($($_.Exception.Message)), falling back to Extend."
        }
    }
    # Adapter IDs change after a reboot or driver update, which invalidates a
    # saved layout. Windows still remembers the last extended arrangement.
    $err = [BigPictureTV.Native]::ApplyExtendTopology()
    if ($err -eq 0) {
        Remove-Item $LayoutFile -ErrorAction SilentlyContinue
        Write-Log 'Restored the last extended layout.'
    } else {
        Write-Log "Restoring the extended layout failed (error $err)."
    }
}

function Set-TvOnly([string]$Name) {
    $layout = [BigPictureTV.Native]::BuildTvOnly($Name)
    if ($null -eq $layout) {
        Write-Log "No connected display matches '$Name'. Run with -ListDisplays to see the names."
        return $false
    }
    $current = [BigPictureTV.Native]::Query([BigPictureTV.Native]::QDC_ONLY_ACTIVE_PATHS)
    if ($current.Paths.Count -eq 1 -and $layout.Modes.Count -gt 0 -and
        $current.Paths[0].targetInfo.id -eq $layout.Paths[0].targetInfo.id) {
        Write-Log 'TV is already the only display.'
        return $true
    }
    # Only save if nothing is saved yet, so a second call never overwrites the
    # real desktop layout with the TV-only one.
    if (-not (Test-Path $LayoutFile)) { Save-Layout }
    $err = [BigPictureTV.Native]::Apply($layout, $ApplyFlags)
    if ($err -ne 0) {
        Write-Log "Switching to the TV failed (error $err)."
        return $false
    }
    Write-Log "Switched to TV only ($Name)."
    return $true
}

function Test-BigPictureOpen {
    if ([BigPictureTV.Native]::AnyWindowTitled($BigPictureTitles)) { return $true }
    foreach ($p in $ExtraProcesses) {
        if (Get-Process -Name $p -ErrorAction SilentlyContinue) { return $true }
    }
    return $false
}

switch ($PSCmdlet.ParameterSetName) {
    'List' {
        [BigPictureTV.Native]::ListDisplays() |
            Select-Object Name, Active, DevicePath |
            Format-Table -AutoSize
        return
    }
    'Restore' { Restore-Layout; return }
    'TvOnly'  { [void](Set-TvOnly $TvName); return }
}

# Watch mode. A single instance per user.
$mutex = New-Object System.Threading.Mutex($false, 'Local\BigPictureTV')
if (-not $mutex.WaitOne(0)) {
    Write-Log 'BigPictureTV is already running.'
    return
}

# If the last run ended while on the TV (crash, sign-out), put things back.
if ((Test-Path $LayoutFile) -and -not (Test-BigPictureOpen)) {
    Write-Log 'Found a leftover saved layout from a previous run.'
    Restore-Layout
}

Write-Log "Watching for Big Picture (TV: '$TvName')."
$onTv = Test-Path $LayoutFile
$closedSince = $null
$attempted = $false   # don't retry a failed switch every poll; wait for the next open

try {
    while ($true) {
        $open = Test-BigPictureOpen
        if ($open) {
            $closedSince = $null
            if (-not $onTv -and -not $attempted) {
                Write-Log 'Big Picture opened.'
                $attempted = $true
                $onTv = Set-TvOnly $TvName
            }
        } elseif (-not $onTv) {
            $attempted = $false
        } else {
            if ($null -eq $closedSince) { $closedSince = Get-Date }
            if (((Get-Date) - $closedSince).TotalSeconds -ge $GraceSeconds) {
                Write-Log 'Big Picture closed.'
                Restore-Layout
                $onTv = $false
                $closedSince = $null
            }
        }
        Start-Sleep -Seconds $PollSeconds
    }
} finally {
    if ($onTv) { Restore-Layout }
    $mutex.ReleaseMutex()
}

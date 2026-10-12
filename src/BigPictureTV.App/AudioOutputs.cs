using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using BigPictureTV.Core.Audio;

namespace BigPictureTV.App;

/// <summary>
/// Lists sound outputs and changes the default one. Listing uses the public
/// Core Audio API; changing the default has no public API, so it uses the
/// IPolicyConfig interface the Windows sound panel itself uses.
/// </summary>
static class AudioOutputs
{
    const int eRender = 0, DEVICE_STATE_ACTIVE = 1, STGM_READ = 0, VT_LPWSTR = 31;
    const int eConsole = 0, eMultimedia = 1, eCommunications = 2, CLSCTX_ALL = 23;
    static PROPERTYKEY FriendlyName = new() { fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"), pid = 14 };

    /// <summary>Active sound outputs. Empty if Windows can't list them.</summary>
    public static IReadOnlyList<AudioDevice> List()
    {
        var result = new List<AudioDevice>();
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out var devices) != 0) return result;
            devices.GetCount(out int count);
            for (int i = 0; i < count; i++)
            {
                if (devices.Item(i, out var device) != 0) continue;
                device.GetId(out string id);
                result.Add(new AudioDevice(id, NameOf(device) ?? id));
            }
        }
        catch (COMException) { }
        return result;
    }

    /// <summary>Id of the current default output, or null.</summary>
    public static string? DefaultId()
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out var device) != 0) return null;
            device.GetId(out string id);
            return id;
        }
        catch (COMException)
        {
            return null;
        }
    }

    /// <summary>Makes an output the default for everything (games, music, calls). False if Windows refused.</summary>
    public static bool SetDefault(string id)
    {
        try
        {
            var policy = (IPolicyConfig)new PolicyConfigClient();
            return policy.SetDefaultEndpoint(id, eConsole) == 0 &
                   policy.SetDefaultEndpoint(id, eMultimedia) == 0 &
                   policy.SetDefaultEndpoint(id, eCommunications) == 0;
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            return false;
        }
    }

    /// <summary>
    /// Moves the volume of the output in use by <paramref name="delta"/> (0 to 1,
    /// so 0.02 is 2%; 0 just reads it). Turning it up unmutes. Null if Windows can't tell.
    /// </summary>
    public static (int Percent, bool Muted)? ChangeVolume(float delta)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            if (enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out var device) != 0) return null;
            var iid = typeof(IAudioEndpointVolume).GUID;
            if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var pointer) != 0 || pointer == IntPtr.Zero) return null;
            try
            {
                var volume = (IAudioEndpointVolume)Marshal.GetObjectForIUnknown(pointer);
                if (volume.GetMasterVolumeLevelScalar(out float level) != 0) return null;
                volume.GetMute(out bool muted);
                if (delta != 0)
                {
                    var context = Guid.Empty;
                    level = Math.Clamp(MathF.Round((level + delta) * 50) / 50, 0, 1); // whole steps of 2%
                    volume.SetMasterVolumeLevelScalar(level, ref context);
                    if (muted && delta > 0 && volume.SetMute(false, ref context) == 0) muted = false;
                }
                return ((int)MathF.Round(level * 100), muted);
            }
            finally
            {
                Marshal.Release(pointer);
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException)
        {
            return null;
        }
    }

    /// <summary>
    /// Moves the volume of the apps whose process <paramref name="isTarget"/>
    /// picks (their own slider in Windows' volume mixer, not the PC's), by
    /// <paramref name="delta"/> like <see cref="ChangeVolume"/>. Null if none of them makes sound.
    /// </summary>
    public static int? ChangeAppVolume(Func<int, bool> isTarget, float delta)
    {
        int? percent = null;
        ForAppSessions(isTarget, allOutputs: false, targets =>
        {
            // The first one sets the level; the others (a browser can have several) follow it.
            targets[0].GetMasterVolume(out float level);
            if (delta != 0)
            {
                var context = Guid.Empty;
                level = Math.Clamp(MathF.Round((level + delta) * 50) / 50, 0, 1);
                foreach (var volume in targets)
                {
                    volume.SetMasterVolume(level, ref context);
                    if (delta > 0) volume.SetMute(false, ref context);
                }
            }
            percent = (int)MathF.Round(level * 100);
        });
        return percent;
    }

    /// <summary>
    /// Puts the apps <paramref name="isTarget"/> picks back to <paramref name="percent"/>,
    /// on every output (they may have moved with the default). Windows keeps
    /// an app's level for the next time it runs, so a level left low here
    /// would stay low for the whole browser.
    /// </summary>
    public static void SetAppVolume(Func<int, bool> isTarget, int percent)
    {
        ForAppSessions(isTarget, allOutputs: true, targets =>
        {
            var context = Guid.Empty;
            foreach (var volume in targets) volume.SetMasterVolume(Math.Clamp(percent / 100f, 0, 1), ref context);
        });
    }

    /// <summary>
    /// The volume controls of the apps <paramref name="isTarget"/> picks, held
    /// so their level can be set after the app closed: Windows still keeps
    /// what is set on a finished session for the app's next run.
    /// </summary>
    public static HeldVolumes HoldAppVolumes(Func<int, bool> isTarget)
    {
        var setters = new List<Func<float, int>>();
        ForAppSessions(isTarget, allOutputs: true, targets =>
        {
            foreach (var volume in targets)
                setters.Add(level => { var context = Guid.Empty; return volume.SetMasterVolume(level, ref context); });
        });
        return new HeldVolumes(setters);
    }

    public sealed class HeldVolumes
    {
        readonly List<Func<float, int>> _setters;

        internal HeldVolumes(List<Func<float, int>> setters) => _setters = setters;

        public int Count => _setters.Count;

        /// <summary>How many took the level.</summary>
        public int Set(int percent)
        {
            int done = 0;
            foreach (var set in _setters)
            {
                try { if (set(Math.Clamp(percent / 100f, 0, 1)) >= 0) done++; }
                catch (Exception e) when (e is COMException or InvalidComObjectException) { }
            }
            return done;
        }
    }

    // Runs `act` once per output, with the target apps' sessions on it.
    static void ForAppSessions(Func<int, bool> isTarget, bool allOutputs, Action<List<ISimpleAudioVolume>> act)
    {
        try
        {
            var enumerator = (IMMDeviceEnumerator)new MMDeviceEnumerator();
            var outputs = new List<IMMDevice>();
            if (allOutputs)
            {
                if (enumerator.EnumAudioEndpoints(eRender, DEVICE_STATE_ACTIVE, out var devices) != 0) return;
                devices.GetCount(out int count);
                for (int i = 0; i < count; i++)
                    if (devices.Item(i, out var device) == 0) outputs.Add(device);
            }
            else if (enumerator.GetDefaultAudioEndpoint(eRender, eMultimedia, out var main) == 0) outputs.Add(main);

            foreach (var device in outputs)
            {
                var iid = typeof(IAudioSessionManager2).GUID;
                if (device.Activate(ref iid, CLSCTX_ALL, IntPtr.Zero, out var pointer) != 0 || pointer == IntPtr.Zero) continue;
                try
                {
                    var manager = (IAudioSessionManager2)Marshal.GetObjectForIUnknown(pointer);
                    if (manager.GetSessionEnumerator(out var sessions) != 0) continue;
                    sessions.GetCount(out int count);
                    var targets = new List<ISimpleAudioVolume>();
                    for (int i = 0; i < count; i++)
                    {
                        if (sessions.GetSession(i, out var session) != 0 || session == null) continue;
                        if (session.GetProcessId(out uint pid) != 0 || pid == 0 || !isTarget((int)pid)) continue;
                        if (session is ISimpleAudioVolume volume) targets.Add(volume);
                    }
                    if (targets.Count > 0) act(targets);
                }
                finally
                {
                    Marshal.Release(pointer);
                }
            }
        }
        catch (Exception e) when (e is COMException or InvalidCastException) { }
    }

    static string? NameOf(IMMDevice device)
    {
        if (device.OpenPropertyStore(STGM_READ, out var store) != 0) return null;
        if (store.GetValue(ref FriendlyName, out var value) != 0) return null;
        try
        {
            return value.vt == VT_LPWSTR ? Marshal.PtrToStringUni(value.pointer) : null;
        }
        finally
        {
            PropVariantClear(ref value);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPERTYKEY
    {
        public Guid fmtid;
        public int pid;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct PROPVARIANT
    {
        public ushort vt, reserved1, reserved2, reserved3;
        public IntPtr pointer, pointer2;
    }

    [DllImport("ole32.dll")]
    static extern int PropVariantClear(ref PROPVARIANT value);

    [ComImport, Guid("BCDE0395-E52F-467C-8E3D-C4579291692E")]
    class MMDeviceEnumerator { }

    [ComImport, Guid("A95664D2-9614-4F35-A746-DE8DB63617E6"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceEnumerator
    {
        [PreserveSig] int EnumAudioEndpoints(int dataFlow, int stateMask, out IMMDeviceCollection devices);
        [PreserveSig] int GetDefaultAudioEndpoint(int dataFlow, int role, out IMMDevice device);
    }

    [ComImport, Guid("0BD7A1BE-7A1A-44DB-8397-CC5392387B5E"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDeviceCollection
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int Item(int index, out IMMDevice device);
    }

    [ComImport, Guid("D666063F-1587-4E43-81F1-B948E807363F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IMMDevice
    {
        [PreserveSig] int Activate(ref Guid iid, int context, IntPtr parameters, out IntPtr result);
        [PreserveSig] int OpenPropertyStore(int access, out IPropertyStore store);
        [PreserveSig] int GetId([MarshalAs(UnmanagedType.LPWStr)] out string id);
    }

    [ComImport, Guid("886d8eeb-8cf2-4446-8d02-cdba1dbdcf99"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPropertyStore
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetAt(int index, out PROPERTYKEY key);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT value);
    }

    // Only the methods up to GetMute are called; the rest of the vtable is left out.
    [ComImport, Guid("5CDF2C82-841E-4546-9722-0CF74078229A"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioEndpointVolume
    {
        [PreserveSig] int RegisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int UnregisterControlChangeNotify(IntPtr notify);
        [PreserveSig] int GetChannelCount(out uint count);
        [PreserveSig] int SetMasterVolumeLevel(float levelDb, ref Guid context);
        [PreserveSig] int SetMasterVolumeLevelScalar(float level, ref Guid context);
        [PreserveSig] int GetMasterVolumeLevel(out float levelDb);
        [PreserveSig] int GetMasterVolumeLevelScalar(out float level);
        [PreserveSig] int SetChannelVolumeLevel(uint channel, float levelDb, ref Guid context);
        [PreserveSig] int SetChannelVolumeLevelScalar(uint channel, float level, ref Guid context);
        [PreserveSig] int GetChannelVolumeLevel(uint channel, out float levelDb);
        [PreserveSig] int GetChannelVolumeLevelScalar(uint channel, out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    // The apps making sound on an output (Windows' volume mixer).
    [ComImport, Guid("77AA99A0-1BD6-484F-8BC7-2C654C9A9B6F"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionManager2
    {
        [PreserveSig] int GetAudioSessionControl(IntPtr groupingParam, int flags, out IntPtr control);
        [PreserveSig] int GetSimpleAudioVolume(IntPtr groupingParam, int flags, out IntPtr volume);
        [PreserveSig] int GetSessionEnumerator(out IAudioSessionEnumerator sessions);
    }

    [ComImport, Guid("E2F5BB11-0570-40CA-ACDD-3AA01277DEE8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionEnumerator
    {
        [PreserveSig] int GetCount(out int count);
        [PreserveSig] int GetSession(int index, out IAudioSessionControl2 session);
    }

    // IAudioSessionControl's methods first, then IAudioSessionControl2's up to GetProcessId.
    [ComImport, Guid("bfb7ff88-7239-4fc9-8fa2-07c950be9c6d"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IAudioSessionControl2
    {
        [PreserveSig] int GetState(out int state);
        [PreserveSig] int GetDisplayName(out IntPtr name);
        [PreserveSig] int SetDisplayName(IntPtr name, ref Guid context);
        [PreserveSig] int GetIconPath(out IntPtr path);
        [PreserveSig] int SetIconPath(IntPtr path, ref Guid context);
        [PreserveSig] int GetGroupingParam(out Guid group);
        [PreserveSig] int SetGroupingParam(ref Guid group, ref Guid context);
        [PreserveSig] int RegisterAudioSessionNotification(IntPtr notify);
        [PreserveSig] int UnregisterAudioSessionNotification(IntPtr notify);
        [PreserveSig] int GetSessionIdentifier(out IntPtr id);
        [PreserveSig] int GetSessionInstanceIdentifier(out IntPtr id);
        [PreserveSig] int GetProcessId(out uint processId);
    }

    [ComImport, Guid("87CE5498-68D6-44E5-9215-6DA47EF883D8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface ISimpleAudioVolume
    {
        [PreserveSig] int SetMasterVolume(float level, ref Guid context);
        [PreserveSig] int GetMasterVolume(out float level);
        [PreserveSig] int SetMute([MarshalAs(UnmanagedType.Bool)] bool mute, ref Guid context);
        [PreserveSig] int GetMute([MarshalAs(UnmanagedType.Bool)] out bool mute);
    }

    [ComImport, Guid("870af99c-171d-4f9e-af0d-e63df40c2bc9")]
    class PolicyConfigClient { }

    // Only SetDefaultEndpoint is called; the methods before it keep the vtable order.
    [ComImport, Guid("f8679f50-850a-41cf-9c72-430f290290c8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IPolicyConfig
    {
        [PreserveSig] int GetMixFormat(IntPtr a, IntPtr b);
        [PreserveSig] int GetDeviceFormat(IntPtr a, int b, IntPtr c);
        [PreserveSig] int ResetDeviceFormat(IntPtr a);
        [PreserveSig] int SetDeviceFormat(IntPtr a, IntPtr b, IntPtr c);
        [PreserveSig] int GetProcessingPeriod(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetProcessingPeriod(IntPtr a, IntPtr b);
        [PreserveSig] int GetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int SetShareMode(IntPtr a, IntPtr b);
        [PreserveSig] int GetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetPropertyValue(IntPtr a, int b, IntPtr c, IntPtr d);
        [PreserveSig] int SetDefaultEndpoint([MarshalAs(UnmanagedType.LPWStr)] string id, int role);
    }
}

using System.Runtime.InteropServices;
using System.Text.Json;

namespace BigPictureTV.Core.Display;

/// <summary>
/// Saves the desktop layout to disk before switching to the TV, so it can be
/// put back later, even after a crash or a sign-out. The raw struct bytes are
/// stored as base64 so the layout is reapplied byte-for-byte.
/// </summary>
public sealed class LayoutStore
{
    readonly string _file;

    public LayoutStore(string file) => _file = file;

    public bool HasSaved => File.Exists(_file);

    public void Save(Layout layout)
    {
        var dto = new SavedLayout
        {
            Saved = DateTimeOffset.Now,
            Paths = ToBase64(layout.Paths),
            Modes = ToBase64(layout.Modes),
        };
        Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
        File.WriteAllText(_file, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
    }

    /// <summary>The saved layout. Throws if the file is missing or unreadable.</summary>
    public Layout Load()
    {
        var dto = JsonSerializer.Deserialize<SavedLayout>(File.ReadAllText(_file))
            ?? throw new FormatException("Saved layout is empty");
        return new Layout { Paths = FromBase64<PATH_INFO>(dto.Paths), Modes = FromBase64<MODE_INFO>(dto.Modes) };
    }

    public void Delete()
    {
        try { File.Delete(_file); } catch (IOException) { }
    }

    // Same JSON shape the PowerShell script writes, so a layout saved by
    // BigPictureTV.ps1 can be restored by the app and vice versa.
    sealed class SavedLayout
    {
        public DateTimeOffset Saved { get; set; }
        public string Paths { get; set; } = "";
        public string Modes { get; set; } = "";
    }

    public static string ToBase64<T>(T[] items) where T : struct
    {
        int size = Marshal.SizeOf<T>();
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

    public static T[] FromBase64<T>(string? text) where T : struct
    {
        int size = Marshal.SizeOf<T>();
        var bytes = Convert.FromBase64String(text ?? "");
        if (bytes.Length % size != 0) throw new FormatException("Saved layout has the wrong size");
        var items = new T[bytes.Length / size];
        IntPtr ptr = Marshal.AllocHGlobal(size);
        try
        {
            for (int i = 0; i < items.Length; i++)
            {
                Marshal.Copy(bytes, i * size, ptr, size);
                items[i] = Marshal.PtrToStructure<T>(ptr);
            }
        }
        finally { Marshal.FreeHGlobal(ptr); }
        return items;
    }
}

namespace BigPictureTV.Core.Audio;

/// <summary>A sound output, as Windows lists it.</summary>
public sealed record AudioDevice(string Id, string Name)
{
    public override string ToString() => Name;
}

/// <summary>Picks which sound output belongs to the TV.</summary>
public static class AudioPicker
{
    /// <summary>
    /// The chosen output if it is still there; otherwise (or with no choice)
    /// the one named after the TV, e.g. "LG TV (NVIDIA High Definition Audio)".
    /// </summary>
    public static AudioDevice? Pick(IReadOnlyList<AudioDevice> outputs, string chosenId, string? tvName)
    {
        if (chosenId.Length > 0)
        {
            var chosen = outputs.FirstOrDefault(d => string.Equals(d.Id, chosenId, StringComparison.OrdinalIgnoreCase));
            if (chosen != null) return chosen;
        }
        if (string.IsNullOrWhiteSpace(tvName)) return null;
        return outputs.FirstOrDefault(d => d.Name.Contains(tvName.Trim(), StringComparison.OrdinalIgnoreCase));
    }
}

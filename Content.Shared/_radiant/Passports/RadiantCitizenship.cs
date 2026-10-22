using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Passports;

[Serializable, NetSerializable]
public enum RadiantCitizenship : byte
{
    Asgard,
    Hesperia,
    Aurum,
    Zon,
    Midgard,
    Avrelia,
    NT,
}

public static class RadiantCitizenships
{
    public static RadiantCitizenship Normalize(RadiantCitizenship value)
        => Enum.IsDefined(value) ? value : RadiantCitizenship.Zon;

    public static RadiantCitizenship Parse(string? value)
    {
        // Older profiles stored the selection lock in this string.
        if (value?.StartsWith("Locked:", StringComparison.Ordinal) == true)
            value = value[7..];
        return Enum.TryParse<RadiantCitizenship>(value, out var parsed)
            ? Normalize(parsed)
            : RadiantCitizenship.Zon;
    }
}

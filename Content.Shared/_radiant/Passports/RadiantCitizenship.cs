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
        => Enum.IsDefined(value) ? value : RadiantCitizenship.Asgard;

    public static bool IsLegacy(string? value)
    {
        if (value?.StartsWith("Locked:", StringComparison.Ordinal) == true)
            value = value[7..];
        return value is "Confederation" or "Empire" or "USSR" or "OPZ";
    }

    public static RadiantCitizenship Parse(string? value)
        => Enum.TryParse<RadiantCitizenship>(value, out var parsed)
            ? Normalize(parsed)
            : RadiantCitizenship.Asgard;
}

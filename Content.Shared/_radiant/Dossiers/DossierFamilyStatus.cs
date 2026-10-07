using Content.Shared.Humanoid;

namespace Content.Shared._radiant.Dossiers;

/// <summary>Stable values stored in character preferences; display text may vary by sex and locale.</summary>
public static class DossierFamilyStatus
{
    public static readonly string[] Values = ["", "single", "married", "partnered", "divorced", "widowed"];

    public static string Normalize(string? value)
    {
        return value?.Trim().ToLowerInvariant() switch
        {
            "single" or "холост" or "не замужем" or "не состоит в браке" => "single",
            "married" or "женат" or "замужем" or "в браке" => "married",
            "partnered" or "в отношениях" => "partnered",
            "divorced" or "разведён" or "разведена" or "разведен" => "divorced",
            "widowed" or "вдовец" or "вдова" => "widowed",
            _ => "",
        };
    }

    public static string LocalizationKey(string? value, Sex sex)
    {
        return Normalize(value) switch
        {
            "single" => sex == Sex.Male ? "radiant-dossier-family-single-male"
                : sex == Sex.Female ? "radiant-dossier-family-single-female"
                : "radiant-dossier-family-single-neutral",
            "married" => sex == Sex.Male ? "radiant-dossier-family-married-male"
                : sex == Sex.Female ? "radiant-dossier-family-married-female"
                : "radiant-dossier-family-married-neutral",
            "partnered" => "radiant-dossier-family-partnered",
            "divorced" => "radiant-dossier-family-divorced",
            "widowed" => "radiant-dossier-family-widowed",
            _ => "radiant-dossier-family-unspecified",
        };
    }
}

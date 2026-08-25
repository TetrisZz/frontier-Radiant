using Content.Shared.Humanoid;
using Content.Shared.Silicons.Borgs.Components;

namespace Content.Shared._Goobstation.Languages;

/// <summary>
/// Shared species-to-language lookup used by speech and written documents.
/// </summary>
public static class SpeciesLanguageUtility
{
    public static string? GetNativeLanguage(IEntityManager entityManager, EntityUid entity)
    {
        if (entityManager.HasComponent<BorgChassisComponent>(entity))
            return "Двоичный";

        if (!entityManager.TryGetComponent(entity, out HumanoidAppearanceComponent? humanoid))
            return null;

        return humanoid.Species.Id switch
        {
            "Reptilian" => "Синта'Унати",
            "Vox" => "Вокс-пиджин",
            "Diona" => "Корневой язык",
            "SlimePerson" => "Бабблилиш",
            "Moth" => "Моффик",
            "Arachnid" => "Щёлкающий",
            "Vulpkanin" => "Канилунц",
            "Tajaran" => "Сиик'тайр",
            "Resomi" => "Счечи",
            "Feroxi" => "Нехина",
            "Shadowkin" => "Сумеречный",
            "Dwarf" => "Кхаздар",
            "Oni" => "Кансэй",
            "Harpy" => "Аэрийский",
            "Goblin" => "Крикли",
            "Sheleg" => "Шелар",
            "DemonSpecies" => "Арканийский",
            "Felinid" => "НекоМетрический",
            _ => null,
        };
    }
}

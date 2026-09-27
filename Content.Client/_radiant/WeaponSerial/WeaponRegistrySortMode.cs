namespace Content.Client._radiant.WeaponSerial;

/// <summary>
///     How the weapon registry list is ordered. This is purely a client-side
///     choice: the whole registry is already in memory, and the server snapshot
///     order (the moment a weapon appeared in the database, newest first) is just
///     the default and the fallback.
/// </summary>
public enum WeaponRegistrySortMode : byte
{
    /// <summary>
    ///     The order the server sent: newest entry first, by the moment the weapon
    ///     appeared in the registry.
    /// </summary>
    DateAdded,

    /// <summary>Owner name, A to Z. Entries with no owner (dash) sink to the bottom.</summary>
    OwnerAscending,

    /// <summary>Owner name, Z to A. Entries with no owner still sink to the bottom.</summary>
    OwnerDescending,

    /// <summary>
    ///     Gun caliber, as it is printed on examine
    ///     (<see cref="Content.Shared.Weapons.Ranged.Components.GunComponent.ExamineCaliber"/>).
    /// </summary>
    Caliber,

    /// <summary>Weapon class from the prototype, e.g. pistol or shotgun.</summary>
    WeaponClass,

    /// <summary>Serial number, the database key of the entry.</summary>
    Serial,
}

public static class WeaponRegistrySortModeExt
{
    /// <summary>
    ///     Fluent id of the mode name. One source of truth for both the option
    ///     buttons inside the sorting window and the filter button's tooltip, so a
    ///     new order is an enum value plus a loc string, nothing else.
    /// </summary>
    public static string GetSortLabel(this WeaponRegistrySortMode mode)
    {
        return mode switch
        {
            WeaponRegistrySortMode.OwnerAscending => "weapon-registry-sort-owner-asc",
            WeaponRegistrySortMode.OwnerDescending => "weapon-registry-sort-owner-desc",
            WeaponRegistrySortMode.Caliber => "weapon-registry-sort-caliber",
            WeaponRegistrySortMode.WeaponClass => "weapon-registry-sort-class",
            WeaponRegistrySortMode.Serial => "weapon-registry-sort-serial",
            _ => "weapon-registry-sort-date",
        };
    }
}

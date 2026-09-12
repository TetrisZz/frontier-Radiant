using Content.Shared._NF.Weapons.Rarity;
using Robust.Shared.Serialization;

namespace Content.Shared.CartridgeLoader.Cartridges;

/// <summary>
///     One entry of the round-scoped weapon registry, shown in the OSK database cartridge page.
///     Owner is entered manually by a player, so it is nullable.
///     PrototypeId lets the client draw the weapon's entity icon in the list.
/// </summary>
[Serializable, NetSerializable]
public sealed record WeaponRegistryEntry(string Serial, string PrototypeId, string Name, WeaponRarity Rarity, string? Owner);

/// <summary>
///     Snapshot of the whole registry, pushed by WeaponSerialSystem to the wanted list cartridge UI.
/// </summary>
[Serializable, NetSerializable]
public sealed class WeaponRegistryUiState(List<WeaponRegistryEntry> entries) : BoundUserInterfaceState
{
    public List<WeaponRegistryEntry> Entries = entries;
}

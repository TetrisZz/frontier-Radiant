using Robust.Shared.Localization;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.WeaponSerial;

/// <summary>
///     UI key of the weapon registration console.
///     Each console has its own "channel" (same idea as the
///     CriminalRecordsConsoleKey of the criminal records console).
/// </summary>
[Serializable, NetSerializable]
public enum WeaponRegistrationConsoleUiKey : byte
{
    Key
}

/// <summary>
///     State of the console window: a summary of the weapon CURRENTLY in the slot.
///     Serial == null means the slot is empty — show the "insert a weapon" hint.
///     Why four separate fields instead of a ready registry entry: name/rarity
///     come from the weapon's prototype in the slot, serial/owner from the
///     registry entry.
/// </summary>
[Serializable, NetSerializable]
public sealed class WeaponRegistrationConsoleState(
    string? serial,
    string? weaponName,
    string? weaponRarity,
    string? owner) : BoundUserInterfaceState
{
    public string? Serial = serial;
    public string? WeaponName = weaponName;
    public string? WeaponRarity = weaponRarity;
    public string? Owner = owner;
}

/// <summary>
///     Message from the console client to the server: "save the owner of the
///     weapon in the slot". The serial is NOT sent — the server looks at the
///     slot itself, so a foreign serial cannot be claimed (security).
/// </summary>
[Serializable, NetSerializable]
public sealed class WeaponRegistrationSetOwner(string? owner) : BoundUserInterfaceMessage
{
    public string? Owner = owner;
}

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
///     WeaponName == null — the slot is empty, show the "insert a weapon" hint.
///     Serial == null — the weapon has no number yet: show the one-time
///     "stamp the number" button.
///     Why separate fields: name/class come from the weapon's prototype in the
///     slot, serial/origin/owner come from the registry entry (the server is
///     the source of truth).
/// </summary>
[Serializable, NetSerializable]
public sealed class WeaponRegistrationConsoleState(
    string? serial,
    string? weaponName,
    string? weaponClass,
    string? origin,
    string? owner) : BoundUserInterfaceState
{
    public string? Serial = serial;
    public string? WeaponName = weaponName;
    /// <summary>Weapon class fluent id (weapon-details-class-*), taken from NFWeaponDetails on the weapon.</summary>
    public string? WeaponClass = weaponClass;
    /// <summary>Origin fluent id (gun-examine-department-*), from the registry entry.</summary>
    public string? Origin = origin;
    public string? Owner = owner;
}

/// <summary>
///     Message for the "stamp the number and enter it into the database" button.
///     The server looks at the slot itself (the client cannot be trusted), and
///     the button only exists while the number is not stamped: after stamping
///     the server sends a state with Serial != null and the client hides it.
/// </summary>
[Serializable, NetSerializable]
public sealed class WeaponRegistrationStampSerial : BoundUserInterfaceMessage
{
}

/// <summary>
///     Message from the console client to the server: "rewrite the owner of the
///     weapon in the slot". The server looks up the weapon by the serial from
///     the slot; if there is no registry entry, an error popup is shown.
///     The serial is NOT sent — the server looks at the slot itself, so a
///     foreign serial cannot be claimed (security).
/// </summary>
[Serializable, NetSerializable]
public sealed class WeaponRegistrationSetOwner(string? owner) : BoundUserInterfaceMessage
{
    public string? Owner = owner;
}

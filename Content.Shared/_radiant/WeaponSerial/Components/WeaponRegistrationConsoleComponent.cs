using Content.Shared.Containers.ItemSlots;
using Robust.Shared.GameStates;

namespace Content.Shared._radiant.WeaponSerial.Components;

/// <summary>
///     Weapon registration console. Accepts a weapon in a slot and
///     automatically registers it in the current round local database.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class WeaponRegistrationConsoleComponent : Component
{
    /// <summary>
    ///     Slot that the weapon is inserted into for registration.
    /// </summary>
    [DataField]
    public ItemSlot WeaponSlot = new();
}


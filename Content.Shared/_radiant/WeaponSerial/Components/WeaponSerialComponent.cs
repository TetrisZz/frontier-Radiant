using Robust.Shared.GameStates;
using Robust.Shared.Localization;

namespace Content.Shared._radiant.WeaponSerial.Components;



/// <summary>
///     Weapon serial number. The component is present on EVERY firearm (it comes
///     with the base gun parent prototype), but the number itself is only stamped
///     later — by a vendor/uplink on sale or manually at the registration console.
///     A null SerialNumber means the number has not been stamped yet: examine
///     shows the honest "wiped" note instead of a number.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class WeaponSerialComponent : Component
{
    /// <summary>
    ///     The weapon's serial number, null means the number has not been stamped yet.
    /// </summary>
    [DataField, AutoNetworkedField]
    public string? SerialNumber;

    /// <summary>
    ///     Where this weapon came from (fluent id): "service weapon of the DVB",
    ///     "civilian weapon (bought on the Lodge)" etc. Stamped together with the
    ///     number by the issuing vendor/uplink and stored in the round registry,
    ///     which is the ONLY place it is shown: the weapon's own examine output
    ///     carries the stamped number alone. Null means the origin is unknown
    ///     (e.g. the number was stamped at the registration console).
    /// </summary>
    [DataField, AutoNetworkedField]
    public LocId? Origin;
}

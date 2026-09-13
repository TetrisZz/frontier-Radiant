namespace Content.Shared._radiant.WeaponSerial.Components;

/// <summary>
///     Marker component: weapons bought in a store carrying it (uplink radio,
///     vending machine, ...) automatically receive a serial number and are
///     entered into the round weapon registry (see WeaponSerialSystem).
///     The list of "serial-issuing" sellers is declared in prototypes (yml),
///     not hardcoded in C# — adding a new seller is a one-line yml change.
///     Inheritance works for free: a child entity gets the component from its
///     parent prototype (e.g. BaseSecurityUplinkRadioDebug inherits it from
///     BaseSecurityUplinkRadio). Uplink tags like SecurityUplink stay untouched —
///     the uplink catalog uses them as listing conditions, not serials.
/// </summary>
[RegisterComponent]
public sealed partial class WeaponSerialVendorComponent : Component
{
}

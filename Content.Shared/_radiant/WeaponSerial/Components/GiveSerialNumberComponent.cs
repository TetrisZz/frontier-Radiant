using System.Text;

namespace Content.Shared._radiant.WeaponSerial.Components;

/// <summary>
/// Seller marker: at a store/vending machine with this component, the purchased weapon
/// receives a printed serial number and is added to the round registry (see WeaponSerialSystem).
/// The list of sellers is defined in prototypes (YAML), not in C# - a new seller is added with a
/// single YAML line.
///
/// In addition to the number, the origin (fluent id) is stamped on the weapon:
/// for DWB uplinks - Service Weapon, for vending loft - Civilian.
/// </summary>
[RegisterComponent]
public sealed partial class GiveSerialNumberComponent : Component
{
    /// <summary>
    /// Fluent id of the origin label, which is embossed on the weapon
    /// together with the number and recorded in the registry. Example:
    /// gun-examine-department-dvb (security/DVB uplink),
    /// gun-examine-department-civilian (loft vending).
    /// </summary>
    [DataField(required: true)]
    public LocId ExamineDepartment = default!;
}

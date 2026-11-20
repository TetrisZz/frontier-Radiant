using Content.Shared.Materials;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Shipyard;

/// <summary>
/// Limits the contents retained in a ship save, in whole items rather than internal material volume.
/// Does not limit how many materials the machine can hold during a round.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ShipSaveMaterialLimitsComponent : Component
{
    [DataField, AutoNetworkedField]
    public int DefaultLimit = 150;

    [DataField, AutoNetworkedField]
    public Dictionary<ProtoId<MaterialPrototype>, int> Limits = new();
}

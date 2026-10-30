using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Careers;

/// <summary>Fleet classification for radar presentation, independent of the vessel's editable name.</summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class CareerRadarComponent : Component
{
    [DataField(required: true), AutoNetworkedField]
    public ProtoId<CareerProfessionPrototype> Profession;
}

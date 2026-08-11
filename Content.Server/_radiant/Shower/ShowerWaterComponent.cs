using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Shower;

[RegisterComponent]
public sealed partial class ShowerWaterComponent : Component
{
    [DataField]
    public ProtoId<ReagentPrototype> Reagent = "Water";

    [DataField]
    public float SpillInterval = 5f;

    [DataField]
    public float WaterPerTile = 0.5f;

    [DataField]
    public int SpillTiles = 3;

    public float Accumulator;
}

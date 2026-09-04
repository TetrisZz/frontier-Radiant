using Robust.Shared.Prototypes;

namespace Content.Server._Starlight.HoloItem;

// Radiant sector: Starlight holographic item projector used by a cyber hand.
[RegisterComponent]
public sealed partial class HoloItemComponent : Component
{
    [DataField(required: true)] public EntProtoId ItemPrototype;
    [DataField] public bool UseOnTarget = true;
    [DataField] public float ChargeUse = 50f;
    [DataField] public List<string> RequiredComponents = new();
}

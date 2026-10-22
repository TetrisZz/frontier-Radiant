using Content.Shared._radiant.ResearchBlueprints;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.ResearchBlueprints;

[RegisterComponent]
public sealed partial class BlueprintResearchSpawnerComponent : Component
{
    [DataField]
    public ProtoId<BlueprintResearchPrototype> Preset = "Default";
}

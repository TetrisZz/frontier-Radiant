using Content.Shared.Radio;
using Content.Shared._radiant.ResearchBlueprints;
using Robust.Shared.Prototypes;

namespace Content.Server.Research.Components;

[RegisterComponent]
public sealed partial class ResearchConsoleComponent : Component
{
    [DataField] public bool BlueprintResearch = true;
    [DataField] public ProtoId<BlueprintResearchPrototype> BlueprintResearchPreset = "Default";
    /// <summary>
    /// The radio channel that the unlock announcements are broadcast to.
    /// </summary>
    [DataField, ViewVariables(VVAccess.ReadWrite)]
    public ProtoId<RadioChannelPrototype> AnnouncementChannel = "Science";
}


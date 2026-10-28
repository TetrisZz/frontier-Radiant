using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Skills;

[Flags]
public enum SkillAction : byte { Use = 1, Interface = 2, Shoot = 4, Pilot = 8, Reload = 16, Pour = 32, Anchor = 64, Interact = 128 }

[Prototype("professionalSkillRequirement")]
public sealed partial class ProfessionalSkillRequirementPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public List<EntProtoId> Targets = new();
    [DataField(required: true)] public ProfessionalSkill Skill;
    [DataField(required: true)] public int Level;
    [DataField] public SkillAction Actions = SkillAction.Interface;
    // Empty means all interface commands, including opening. Otherwise only these commands are gated.
    [DataField] public List<string> UiMessages = new();
}

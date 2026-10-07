using System.Linq;
using Content.Shared._radiant.Skills;

namespace Content.Shared.Preferences;

public sealed partial class HumanoidCharacterProfile
{
    [DataField]
    public int[] SkillLevels { get; private set; } = new int[ProfessionalSkillRules.Count];

    public int SkillPointsSpent => SkillLevels.Sum();

    public HumanoidCharacterProfile WithSkillLevel(ProfessionalSkill skill, int level)
    {
        var index = (int) skill;
        if (index >= ProfessionalSkillRules.Count || level < 0 || level > ProfessionalSkillRules.Maximum(skill))
            return this;
        var levels = ProfessionalSkillRules.Normalize(SkillLevels);
        levels[index] = level;
        if (levels.Sum() > ProfessionalSkillRules.Budget)
            return this;
        return new HumanoidCharacterProfile(this) { SkillLevels = levels };
    }

    public HumanoidCharacterProfile WithSkillLevels(int[]? levels)
        => new(this) { SkillLevels = ProfessionalSkillRules.Normalize(levels) };
}

using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Skills;

// Values are persisted in profiles. Append new skills; never reorder existing ones.
[Serializable, NetSerializable]
public enum ProfessionalSkill : byte
{
    Piloting, Shooting, Medicine, Engineering, Salvage, Botany, Cooking, Science
}

public static class ProfessionalSkillRules
{
    public const int Budget = 10;
    public const int Count = 8;

    public static int Maximum(ProfessionalSkill skill) => skill switch
    {
        ProfessionalSkill.Piloting or ProfessionalSkill.Shooting or ProfessionalSkill.Engineering
            or ProfessionalSkill.Botany or ProfessionalSkill.Cooking => 3,
        ProfessionalSkill.Medicine or ProfessionalSkill.Salvage => 4,
        ProfessionalSkill.Science => 2,
        _ => 0,
    };

    public static string NameKey(ProfessionalSkill skill) => $"professional-skill-{skill.ToString().ToLowerInvariant()}";

    public static int CookingLevel(int distinctIngredients) => distinctIngredients <= 2 ? 1 : distinctIngredients <= 4 ? 2 : 3;

    public static int[] Normalize(int[]? source)
    {
        var result = new int[Count];
        var remaining = Budget;
        for (var i = 0; i < Count; i++)
        {
            var level = source != null && i < source.Length ? source[i] : 0;
            result[i] = Math.Clamp(level, 0, Math.Min(Maximum((ProfessionalSkill) i), remaining));
            remaining -= result[i];
        }
        return result;
    }
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class ProfessionalSkillsComponent : Component
{
    [DataField, AutoNetworkedField]
    public int[] Levels = new int[ProfessionalSkillRules.Count];
}

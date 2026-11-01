using Content.Shared._radiant.Skills;
using Robust.Client.Player;
using Robust.Shared.IoC;

namespace Content.Client._radiant.Skills;

public static class SkillPresentation
{
    public static bool CanIdentifyChemicals
    {
        get
        {
            var user = IoCManager.Resolve<IPlayerManager>().LocalEntity;
            return user == null || IoCManager.Resolve<IEntityManager>().System<SharedProfessionalSkillsSystem>()
                .Level(user.Value, ProfessionalSkill.Medicine) >= 2;
        }
    }

    public static bool CanIdentifyReagent(string? id)
    {
        var user = IoCManager.Resolve<IPlayerManager>().LocalEntity;
        return user == null || IoCManager.Resolve<IEntityManager>().System<SharedProfessionalSkillsSystem>()
            .CanIdentifyReagent(user.Value, id);
    }

    public static string ChemicalName(string name, string? id = null)
        => CanIdentifyReagent(id) ? name : Loc.GetString("professional-skills-unknown-reagent");

    public static Color ChemicalColor(Color color, string? id = null)
        => CanIdentifyReagent(id) ? color : Color.Gray;
}

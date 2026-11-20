using Content.Shared._radiant.Skills;
using Robust.Client.Player;
using Robust.Shared.IoC;

namespace Content.Client._radiant.Skills;

public static class SkillPresentation
{
    public static Color SkillColor(ProfessionalSkill skill) => Color.FromHex(skill switch
    {
        ProfessionalSkill.Piloting => "#AA91F2",
        ProfessionalSkill.Shooting => "#F08080",
        ProfessionalSkill.Medicine => "#66D5E8",
        ProfessionalSkill.Engineering => "#F3CB70",
        ProfessionalSkill.Salvage => "#DBAA75",
        ProfessionalSkill.Botany => "#85D989",
        ProfessionalSkill.Cooking => "#E6B78E",
        _ => "#CDA7ED",
    });

    public static bool CanIdentifyChemicals
    {
        get
        {
            var user = IoCManager.Resolve<IPlayerManager>().LocalEntity;
            return user == null || IoCManager.Resolve<IEntityManager>().System<SharedProfessionalSkillsSystem>()
                .CanIdentifyChemicals(user.Value);
        }
    }

    public static bool CanMixChemicals
    {
        get
        {
            var user = IoCManager.Resolve<IPlayerManager>().LocalEntity;
            return user == null || IoCManager.Resolve<IEntityManager>().System<SharedProfessionalSkillsSystem>()
                .CanMixChemicals(user.Value);
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

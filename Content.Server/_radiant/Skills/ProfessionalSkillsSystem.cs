using Content.Shared._radiant.Skills;
using Content.Shared.GameTicking;

namespace Content.Server._radiant.Skills;

public sealed class ProfessionalSkillsSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawn);
        SubscribeLocalEvent<ProfessionalSkillsComponent, Content.Shared.Cloning.Events.CloningEvent>(OnCloned);
    }

    private void OnSpawn(PlayerSpawnCompleteEvent args)
    {
        var skills = EnsureComp<ProfessionalSkillsComponent>(args.Mob);
        // Snapshot at spawn: saving a new lobby build cannot change the current character.
        skills.Levels = ProfessionalSkillRules.Normalize(args.Profile.SkillLevels);
        Dirty(args.Mob, skills);
    }

    private void OnCloned(Entity<ProfessionalSkillsComponent> ent, ref Content.Shared.Cloning.Events.CloningEvent args)
    {
        var skills = EnsureComp<ProfessionalSkillsComponent>(args.CloneUid);
        skills.Levels = ProfessionalSkillRules.Normalize(ent.Comp.Levels);
        Dirty(args.CloneUid, skills);
    }
}

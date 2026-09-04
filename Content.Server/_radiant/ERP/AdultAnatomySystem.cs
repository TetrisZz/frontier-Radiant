using Content.Shared._radiant.ERP;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;

namespace Content.Server._radiant.ERP;

/// <summary>Radiant sector: initializes ERP anatomy once from the character's sex.</summary>
public sealed class AdultAnatomySystem : EntitySystem
{
    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HumanoidAppearanceComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<HumanoidAppearanceComponent, SexChangedEvent>(OnSexChanged);
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnPlayerSpawnComplete);
    }

    private void OnMapInit(Entity<HumanoidAppearanceComponent> ent, ref MapInitEvent args)
    {
        var anatomy = EnsureComp<AdultAnatomyComponent>(ent.Owner);
        if (anatomy.AnatomyInitialized && anatomy.SurgicallyModified)
            return;

        InitializeFromSex(ent.Owner, anatomy, ent.Comp.Sex);
    }

    private void OnSexChanged(EntityUid uid, HumanoidAppearanceComponent component, SexChangedEvent args)
    {
        var anatomy = EnsureComp<AdultAnatomyComponent>(uid);
        if (anatomy.SurgicallyModified)
            return;

        // Radiant sector: the character profile is applied after MapInit for
        // player mobs, while HumanoidAppearance defaults to Male.
        InitializeFromSex(uid, anatomy, args.NewSex);
    }

    private void OnPlayerSpawnComplete(PlayerSpawnCompleteEvent args)
    {
        var anatomy = EnsureComp<AdultAnatomyComponent>(args.Mob);
        if (anatomy.SurgicallyModified)
            return;

        // Radiant sector: authoritative final correction after the selected
        // profile has been loaded onto a newly spawned player.
        InitializeFromSex(args.Mob, anatomy, args.Profile.Sex);
    }

    private void InitializeFromSex(EntityUid uid, AdultAnatomyComponent anatomy, Sex sex)
    {
        anatomy.AnatomyInitialized = true;
        anatomy.HasPenis = false;
        anatomy.PenisSurgicallyRemoved = false;
        anatomy.HasVagina = false;
        anatomy.VaginaSurgicallyRemoved = false;
        anatomy.PenisNervesIntact = true;
        anatomy.HasBreasts = false;
        anatomy.BreastsSurgicallyRemoved = false;
        anatomy.BreastSize = AdultBreastSize.Medium;
        anatomy.BreastSizeSurgicallyChanged = false;

        switch (sex)
        {
            case Sex.Male:
                anatomy.HasPenis = true;
                break;
            case Sex.Female:
                anatomy.HasVagina = true;
                anatomy.HasBreasts = true;
                anatomy.BreastSize = AdultBreastSize.Medium;
                break;
        }

        Dirty(uid, anatomy);
    }
}

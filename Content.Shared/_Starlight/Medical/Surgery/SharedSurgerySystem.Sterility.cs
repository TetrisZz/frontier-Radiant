using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared._Starlight.Medical.Limbs;
using Content.Shared.Humanoid;
using Content.Shared.Body.Part;

namespace Content.Shared._Starlight.Medical.Surgery;

public abstract partial class SharedSurgerySystem
{
    private void OnExtractGlassValid(Entity<SurgeryExtractGlassComponent> ent, ref SurgeryValidEvent args)
    {
        args.Cancelled |= !TryComp<EmbeddedGlassComponent>(args.Part, out var glass) || glass.Fragments.Count == 0;
    }

    public bool UsesSurgicalSterility(EntityUid body, EntityUid part)
        => !HasComp<ReversibleCyberLimbComponent>(part)
            && TryComp<HumanoidAppearanceComponent>(body, out var appearance)
            && appearance.Species.Id != "IPC";

    public bool IsSurgicalSlime(EntityUid body)
        => TryComp<HumanoidAppearanceComponent>(body, out var appearance)
            && appearance.Species.Id is "SlimePerson" or "NeoSlimePerson";

    protected void OpenLimbAttachmentWound(EntityUid body, EntityUid part)
    {
        if (!UsesSurgicalSterility(body, part))
            return;

        // The attachment socket is an open surface wound even when no torso
        // cavity was opened. Keep irrigation and normal incision closure available.
        EnsureComp<IncisionOpenComponent>(part);
        var progress = EnsureComp<SurgeryProgressComponent>(part);
        progress.CompletedSurgeries.Add(IsSurgicalSlime(body) ? "SurgeryOpenIncisionSlime" : "SurgeryOpenIncision");
        Dirty(part, progress);
    }

    public bool IsSurgicalSiteOpen(EntityUid part, SurgicalSite site)
        => site == SurgicalSite.Surface ? HasComp<IncisionOpenComponent>(part)
            : TryComp<SurgicalCavityStateComponent>(part, out var cavities) && site switch
            {
                SurgicalSite.Ribcage => cavities.RibcageOpen,
                SurgicalSite.Abdomen => cavities.AbdomenOpen,
                SurgicalSite.Groin => cavities.GroinOpen,
                _ => false,
            };

    private void OnSiteTreatmentValid(Entity<SurgerySiteTreatmentComponent> ent, ref SurgeryValidEvent args)
    {
        // Shared steps describe the effect, not its anatomical target. The operation
        // supplies that target; validating the step would also require Surface.
        if (!HasComp<SurgeryComponent>(ent))
            return;
        if (!UsesSurgicalSterility(args.Body, args.Part))
        {
            args.Cancelled = true;
            return;
        }
        TryComp<SurgicalSterilityComponent>(args.Part, out var state);
        if (ent.Comp.Site == SurgicalSite.Surface && ent.Comp.Treatment == SurgicalTreatment.Drape
            && TryComp<BodyPartComponent>(args.Part, out var part) && part.PartType == BodyPartType.Torso)
        {
            args.Cancelled = true;
            return;
        }
        args.Cancelled |= ent.Comp.Treatment switch
        {
            SurgicalTreatment.Drape => state?.Draped.Contains(ent.Comp.Site) == true,
            SurgicalTreatment.RemoveDrape => state?.Draped.Contains(ent.Comp.Site) != true,
            SurgicalTreatment.Lavage => !IsSurgicalSiteOpen(args.Part, ent.Comp.Site) || state == null
                || state.Lavaged.Contains(ent.Comp.Site)
                || state.Infection.GetValueOrDefault(ent.Comp.Site) == 0 && state.Contamination.GetValueOrDefault(ent.Comp.Site) < 25,
            SurgicalTreatment.Debride => !IsSurgicalSiteOpen(args.Part, ent.Comp.Site)
                || state?.NecrosisSeconds.ContainsKey(ent.Comp.Site) != true,
            SurgicalTreatment.RestoreHeart => !IsSurgicalSiteOpen(args.Part, SurgicalSite.Ribcage)
                || !HasRepairableHeart(args.Part),
            _ => true,
        };
    }

    public bool HasRepairableHeart(EntityUid part)
    {
        foreach (var organ in _body.GetPartOrgans(part))
        {
            if (HasComp<OrganHeartComponent>(organ.Id)
                && TryComp<SurgicalOrganNecrosisComponent>(organ.Id, out var necrosis)
                && !necrosis.Dead && necrosis.Seconds > 0)
                return true;
        }
        return false;
    }

    private void OnSiteTreatmentCanPerform(Entity<SurgerySiteTreatmentComponent> ent, ref SurgeryCanPerformStepEvent args)
    {
        if (ent.Comp.Treatment != SurgicalTreatment.Drape)
            return;
        foreach (var tool in args.Tools)
        {
            if (!HasComp<SurgicalDrapeComponent>(tool))
                continue;
            if (TryComp<SurgicalItemSterilityComponent>(tool, out var state) && state.Dirty)
            {
                args.Invalid = StepInvalidReason.DirtyDrape;
                args.Popup = Loc.GetString("surgical-warning-drape");
                return;
            }
            return;
        }
    }

    private void OnDisinfectionValid(Entity<SurgeryDisinfectionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!HasComp<SurgeryComponent>(ent))
            return;
        if (!UsesSurgicalSterility(args.Body, args.Part))
        {
            args.Cancelled = true;
            return;
        }
        if (ent.Comp.Site != SurgicalSite.Surface)
        {
            args.Cancelled |= !IsSurgicalSiteOpen(args.Part, ent.Comp.Site);
            return;
        }
        // Skin can be prepared before cutting. Existing contamination inside a closed
        // incision cannot be washed out through intact skin: reopen it first.
        if (!TryComp<SurgicalSterilityComponent>(args.Part, out var state)
            || state.Contamination.GetValueOrDefault(ent.Comp.Site) == 0)
            return;
        var open = ent.Comp.Site == SurgicalSite.Surface
            ? HasComp<IncisionOpenComponent>(args.Part)
            : TryComp<SurgicalCavityStateComponent>(args.Part, out var cavities) && ent.Comp.Site switch
            {
                SurgicalSite.Ribcage => cavities.RibcageOpen,
                SurgicalSite.Abdomen => cavities.AbdomenOpen,
                SurgicalSite.Groin => cavities.GroinOpen,
                _ => false,
            };
        if (!open)
            args.Cancelled = true;
    }

    public SurgicalSite GetSurgicalSite(EntityUid surgery)
        => FindSurgicalSite(surgery, new HashSet<EntityUid>()) ?? SurgicalSite.Surface;

    public SurgicalSite GetSurgicalSite(EntityUid surgery, EntityUid step)
    {
        // Reconstruction spans two cavities; its groin prerequisite must not
        // redirect contamination from a breast transplant into the groin.
        if (HasComp<Content.Shared._radiant.Medical.Surgery.SurgeryChangeSexComponent>(surgery)
            && TryComp<SurgeryStepAdultOrganComponent>(step, out var organ)
            && organ.Organ == Content.Shared._radiant.ERP.AdultOrganType.Breasts)
            return SurgicalSite.Ribcage;

        return GetSurgicalSite(surgery);
    }

    private SurgicalSite? FindSurgicalSite(EntityUid surgery, HashSet<EntityUid> visited)
    {
        if (!visited.Add(surgery))
            return null;
        if (TryComp<SurgerySiteTreatmentComponent>(surgery, out var treatment))
            return treatment.Site;
        if (TryComp<SurgeryDisinfectionComponent>(surgery, out var preparation))
            return preparation.Site;
        if (TryComp<SurgeryCavityConditionComponent>(surgery, out var cavity))
            return cavity.Cavity switch
            {
                SurgicalCavity.Ribcage => SurgicalSite.Ribcage,
                SurgicalCavity.Abdomen => SurgicalSite.Abdomen,
                SurgicalCavity.Groin => SurgicalSite.Groin,
                _ => SurgicalSite.Surface,
            };
        if (TryComp<SurgeryComponent>(surgery, out var operation))
        {
            foreach (var requirement in operation.Requirement)
            {
                if (_entitySystem.TryGetSingleton(requirement, out var required)
                    && FindSurgicalSite(required, visited) is { } site)
                    return site;
            }
        }
        return null;
    }

    protected virtual void ApplySurgicalContamination(EntityUid surgery, EntityUid step,
        ref SurgeryStepCompleteEvent args, HashSet<EntityUid> usedTools, List<EntityUid> heldBefore) { }

    protected virtual void WarnSurgicalRisk(EntityUid user, EntityUid body, EntityUid part, EntityUid step, HashSet<EntityUid> tools) { }

    protected virtual void OnSurgicalFailure(EntityUid user, EntityUid body, EntityUid part, float successRate) { }
}

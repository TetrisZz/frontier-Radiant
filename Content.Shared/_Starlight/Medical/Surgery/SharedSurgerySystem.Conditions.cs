using Content.Shared.Body.Part;
using Content.Shared.Humanoid;
using BodyPartType = Content.Shared.Body.Part.BodyPartType;
using System.Linq;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared.Body.Systems;
using Content.Shared._Starlight.Medical.Body.Part;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._radiant;
using Content.Shared._radiant.ERP;
using Content.Shared.DetailExaminable;

namespace Content.Shared._Starlight.Medical.Surgery;
// Based on the RMC14.
// https://github.com/RMC-14/RMC-14
public abstract partial class SharedSurgerySystem
{
    protected List<Type> _accents = [];
    private void InitializeConditions()
    {
        _accents = _reflectionManager.FindTypesWithAttribute<RegisterComponentAttribute>()
            .Where(type => type.Name.EndsWith("AccentComponent"))
            .ToList();

        SubscribeLocalEvent<SurgeryPartConditionComponent, SurgeryValidEvent>(OnPartConditionValid);
        SubscribeLocalEvent<SurgeryCavityConditionComponent, SurgeryValidEvent>(OnCavityConditionValid);
        SubscribeLocalEvent<SurgerySpeciesConditionComponent, SurgeryValidEvent>(OnSpeciesConditionValid);
        SubscribeLocalEvent<SurgeryOrganExistConditionComponent, SurgeryValidEvent>(OnOrganExistConditionValid);
        SubscribeLocalEvent<SurgeryOrganDontExistConditionComponent, SurgeryValidEvent>(OnOrganDontExistConditionValid);
        SubscribeLocalEvent<SurgeryAnyAccentConditionComponent, SurgeryValidEvent>(OnAnyAccentConditionValid);
        SubscribeLocalEvent<SurgeryAnyLimbSlotConditionComponent, SurgeryValidEvent>(OnAnyLimbSlotConditionValid);
        SubscribeLocalEvent<SurgeryLimbSlotConditionComponent, SurgeryValidEvent>(OnLimbSlotConditionValid);
        SubscribeLocalEvent<SurgeryHasCompConditionComponent, SurgeryValidEvent>(OnHasCompConditionValid);
        // Radiant sector: adult operations use the same opt-out and real-anatomy state as the ERP panel.
        SubscribeLocalEvent<SurgeryErpConsentConditionComponent, SurgeryValidEvent>(OnErpConsentConditionValid);
        SubscribeLocalEvent<SurgeryAdultOrganConditionComponent, SurgeryValidEvent>(OnAdultOrganConditionValid);
        SubscribeLocalEvent<SurgeryAdultBreastSizeConditionComponent, SurgeryValidEvent>(OnAdultBreastSizeConditionValid);
        SubscribeLocalEvent<SurgeryPenisNerveConditionComponent, SurgeryValidEvent>(OnPenisNerveConditionValid);
        SubscribeLocalEvent<SurgeryPenisInstallationConditionComponent, SurgeryValidEvent>(OnPenisInstallationConditionValid);
    }

    private void OnCavityConditionValid(Entity<SurgeryCavityConditionComponent> ent, ref SurgeryValidEvent args)
    {
        var isOpen = TryComp<SurgicalCavityStateComponent>(args.Part, out var cavities)
                     && cavities.IsOpen(ent.Comp.Cavity);
        if (isOpen != ent.Comp.Open)
            args.Cancelled = true;
    }

    private void OnErpConsentConditionValid(Entity<SurgeryErpConsentConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (TryComp<DetailExaminableComponent>(args.Body, out var detail) && detail.ERPStatus == EnumERPStatus.NO)
            args.Cancelled = true;
    }

    private void OnAdultOrganConditionValid(Entity<SurgeryAdultOrganConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!TryComp<AdultAnatomyComponent>(args.Body, out var anatomy)
            || AdultAnatomyHelpers.HasOrgan(anatomy, ent.Comp.Organ) != ent.Comp.Present)
            args.Cancelled = true;
    }

    private void OnAdultBreastSizeConditionValid(Entity<SurgeryAdultBreastSizeConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!TryComp<AdultAnatomyComponent>(args.Body, out var anatomy)
            || !anatomy.HasBreasts
            || anatomy.BreastSize == ent.Comp.DisallowedSize)
            args.Cancelled = true;
    }

    private void OnPenisNerveConditionValid(Entity<SurgeryPenisNerveConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!TryComp<AdultAnatomyComponent>(args.Body, out var anatomy)
            || !anatomy.HasPenis
            || !anatomy.PenisNervesIntact)
            args.Cancelled = true;
    }

    private void OnPenisInstallationConditionValid(Entity<SurgeryPenisInstallationConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!TryComp<AdultAnatomyComponent>(args.Body, out var anatomy)
            || anatomy.HasPenis
            || anatomy.HasVagina)
            args.Cancelled = true;
    }

    private void OnHasCompConditionValid(Entity<SurgeryHasCompConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (ent.Comp.Component == null)
            return; // nothing to check

        foreach (var comp in (ent.Comp.Component ?? []).Values)
            if (!EntityManager.HasComponent(args.Body, comp.Component.GetType()))
            {
                args.Cancelled = true;
                return;
            }
    }

    private void OnOrganDontExistConditionValid(Entity<SurgeryOrganDontExistConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (ent.Comp.Organ?.Count != 1) return;
        var type = ent.Comp.Organ.Values.First().Component.GetType();

        if (ent.Comp.Container != null)
        {
            var containerId = SharedBodySystem.GetOrganContainerId(ent.Comp.Container);
            if (!_containers.TryGetContainer(args.Part, containerId, out var container))
                return;

            foreach (var containedEnt in container.ContainedEntities)
            {
                if (HasComp(containedEnt, type))
                {
                    args.Cancelled = true;
                    return;
                }
            }
        }
        else
        {
            var organs = _body.GetPartOrgans(args.Part, Comp<BodyPartComponent>(args.Part));
            foreach (var organ in organs)
                if (HasComp(organ.Id, type))
                {
                    args.Cancelled = true;
                    return;
                }
        }
    }
    private void OnOrganExistConditionValid(Entity<SurgeryOrganExistConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (ent.Comp.Organ?.Count != 1) return;

        var type = ent.Comp.Organ.Values.First().Component.GetType();

        EntityUid mainPart = args.Part;

        if (TryComp<BodyPartComponent>(args.Body, out var itemPart))
            mainPart = args.Body;

        if (ent.Comp.Container != null)
        {
            var containerId = SharedBodySystem.GetOrganContainerId(ent.Comp.Container);
            if (!_containers.TryGetContainer(mainPart, containerId, out var container))
            {
                args.Cancelled = true;
                return;
            }

            foreach (var containedEnt in container.ContainedEntities)
                if (HasComp(containedEnt, type))
                    return;

            args.Cancelled = true;
        }
        else
        {
            var organs = _body.GetPartOrgans(mainPart, Comp<BodyPartComponent>(mainPart));
            foreach (var organ in organs)
                if (HasComp(organ.Id, type))
                    return;
            args.Cancelled = true;
        }
    }

    private void OnPartConditionValid(Entity<SurgeryPartConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (ent.Comp.Parts.Count == 0)
            return;

        if (TryComp<BodyPartComponent>(args.Body, out var itemPart) && itemPart.PartType is BodyPartType item && !ent.Comp.Parts.Contains(item))
        {
            Log.Warning("don't have part at part");
            args.Cancelled = true;
        }

        if (CompOrNull<BodyPartComponent>(args.Part)?.PartType is BodyPartType part && !ent.Comp.Parts.Contains(part))
            args.Cancelled = true;
    }
    private void OnSpeciesConditionValid(Entity<SurgerySpeciesConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (!EntityManager.TryGetComponent<HumanoidAppearanceComponent>(args.Body, out var humanoidAppearanceComponent))
        {
            args.Cancelled = true;
            return;
        }

        if (ent.Comp.SpeciesBlacklist.Contains(humanoidAppearanceComponent.Species))
        {
            args.Cancelled = true;
            return;
        }

        if (ent.Comp.SpeciesWhitelist.Count > 0 && !ent.Comp.SpeciesWhitelist.Contains(humanoidAppearanceComponent.Species))
        {
            args.Cancelled = true;
            return;
        }
    }
    private void OnAnyAccentConditionValid(Entity<SurgeryAnyAccentConditionComponent> ent, ref SurgeryValidEvent args)
    {
        foreach (var accent in _accents)
            if (HasComp(args.Body, accent))
                return;
        args.Cancelled = true;
    }
    private void OnAnyLimbSlotConditionValid(Entity<SurgeryAnyLimbSlotConditionComponent> ent, ref SurgeryValidEvent args)
    {
        if (CompOrNull<BodyPartComponent>(args.Part) is not BodyPartComponent bodyPartComponent)
            return;

        foreach (var slotId in bodyPartComponent.Children.Keys)
        {
            if (_containers.TryGetContainer(args.Part, SharedBodySystem.GetPartSlotContainerId(slotId), out var container)
                && container.ContainedEntities.Count == 0)
            {
                args.Suffix = slotId;
                return;
            }
        }

        args.Cancelled = true;
    }
    private void OnLimbSlotConditionValid(Entity<SurgeryLimbSlotConditionComponent> ent, ref SurgeryValidEvent args)
    {
        // Radiant sector: Frontier body prototypes use both "left_arm" and
        // "left arm" slot spellings. Match their semantic name so an empty arm
        // or leg socket is visible to the attachment surgery in every species.
        if (!TryComp<BodyPartComponent>(args.Part, out var part))
        {
            args.Cancelled = true;
            return;
        }

        var requested = NormalizeLimbSlot(ent.Comp.Slot);
        var actualSlot = part.Children.Keys.FirstOrDefault(slot => NormalizeLimbSlot(slot) == requested);
        args.Cancelled = actualSlot == null
            || !_containers.TryGetContainer(args.Part, SharedBodySystem.GetPartSlotContainerId(actualSlot), out var container)
            || container.ContainedEntities.Count != 0;
    }

    private static string NormalizeLimbSlot(string slot)
        => slot.Replace('_', ' ').ToLowerInvariant();
}

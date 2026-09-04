using System.Linq;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Traits.Assorted;
using Content.Shared.Bed.Sleep;
using Robust.Shared.Timing;
using Content.Shared.Damage;
using Content.Shared._Starlight;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Robust.Shared.Prototypes;
using Content.Shared._radiant.ERP;
using Content.Shared.Animals;

namespace Content.Server._Starlight.Medical.Surgery;
// Based on the RMC14.
// https://github.com/RMC-14/RMC-14
//
//This file is already overloaded with responsibilities,
//it’s time to break its functionality into different systems.
//However, I don’t want to touch the official systems, so I need to come up with extensions for them.
public sealed partial class SurgerySystem : SharedSurgerySystem
{
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private StarlightEntitySystem _entity = default!;
    [Dependency] private SharedBloodstreamSystem _bloodstreamSystem = default!;
    [Dependency] private SharedTransformSystem _transform = default!;

    public void InitializeSteps()
    {
        SubscribeLocalEvent<SurgeryStepBleedEffectComponent, SurgeryStepEvent>(OnStepBleedComplete);
        SubscribeLocalEvent<SurgeryClampBleedEffectComponent, SurgeryStepEvent>(OnStepClampBleedComplete);
        SubscribeLocalEvent<SurgeryStepEmoteEffectComponent, SurgeryStepEvent>(OnStepEmoteEffectComplete);
        SubscribeLocalEvent<SurgeryStepSpawnEffectComponent, SurgeryStepEvent>(OnStepSpawnComplete);

        SubscribeLocalEvent<SurgeryStepOrganExtractComponent, SurgeryStepEvent>(OnStepOrganExtractComplete);
        SubscribeLocalEvent<SurgeryStepOrganInsertComponent, SurgeryStepEvent>(OnStepOrganInsertComplete);
        SubscribeLocalEvent<SurgeryStepAdultOrganComponent, SurgeryStepEvent>(OnStepAdultOrganComplete);
        SubscribeLocalEvent<SurgeryStepCavityEffectComponent, SurgeryStepEvent>(OnStepCavityComplete);

        SubscribeLocalEvent<SurgeryStepAttachLimbEffectComponent, SurgeryStepEvent>(OnStepAttachComplete);
        SubscribeLocalEvent<SurgeryStepAmputationEffectComponent, SurgeryStepEvent>(OnStepAmputationComplete);

        SubscribeLocalEvent<CustomLimbMarkerComponent, ComponentRemove>(CustomLimbRemoved);

        SubscribeLocalEvent<SurgeryRemoveAccentComponent, SurgeryStepEvent>(OnRemoveAccent);

    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<SurgicalCavityStateComponent>();
        while (query.MoveNext(out var uid, out var cavities))
        {
            var openCount = (cavities.RibcageOpen ? 1 : 0)
                            + (cavities.AbdomenOpen ? 1 : 0)
                            + (cavities.GroinOpen ? 1 : 0);
            if (openCount == 0)
                continue;

            var incision = EnsureComp<IncisionOpenComponent>(uid);
            if (_timing.CurTime < incision.NextUpdate)
                continue;

            incision.NextUpdate = _timing.CurTime + incision.UpdateInterval;

            if (!TryComp<BodyPartComponent>(uid, out var part) || part.Body is not { } patient)
                continue;

            _bloodstreamSystem.TryModifyBleedAmount(patient, 0.1f * openCount);
        }

        // Radiant sector: head and detached-limb incisions still use the legacy
        // marker and must keep their original bleeding behaviour.
        var legacyQuery = EntityQueryEnumerator<IncisionOpenComponent>();
        while (legacyQuery.MoveNext(out var uid, out var incision))
        {
            if (TryComp<SurgicalCavityStateComponent>(uid, out var cavities)
                && (cavities.RibcageOpen || cavities.AbdomenOpen || cavities.GroinOpen)
                || _timing.CurTime < incision.NextUpdate)
                continue;

            incision.NextUpdate = _timing.CurTime + incision.UpdateInterval;
            if (TryComp<BodyPartComponent>(uid, out var part) && part.Body is { } patient)
                _bloodstreamSystem.TryModifyBleedAmount(patient, 0.1f);
        }
    }

    private void OnStepCavityComplete(Entity<SurgeryStepCavityEffectComponent> ent, ref SurgeryStepEvent args)
    {
        var cavities = EnsureComp<SurgicalCavityStateComponent>(args.Part);
        cavities.SetOpen(ent.Comp.Cavity, ent.Comp.Open);
        Dirty(args.Part, cavities);

        // Once every independent torso incision is closed there is no residual
        // visible wound. A new unfinished incision will add the marker again.
        if (!cavities.RibcageOpen && !cavities.AbdomenOpen && !cavities.GroinOpen)
            RemComp<IncisionOpenComponent>(args.Part);
    }

    private void OnStepAttachComplete(Entity<SurgeryStepAttachLimbEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (!_entity.TryGetSingleton(args.SurgeryProto, out var surgery)
            || !TryComp<SurgeryLimbSlotConditionComponent>(surgery, out var slotComp))
            return;

        OnStepAttachLimbComplete(ent, slotComp.Slot, ref args);
        if (slotComp.Slot != "head" && args.IsCancelled)
            OnStepAttachItemComplete(ent, slotComp.Slot, ref args);
    }

    private void OnStepBleedComplete(Entity<SurgeryStepBleedEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (ent.Comp.Damage == null)
            return;
        var damage = ent.Comp.Damage;
        if (ent.Comp.Damage is not null && TryComp<DamageableComponent>(args.Body, out var comp))
            _damageableSystem.TryChangeDamage(args.Body, damage);
    }

    private void OnStepClampBleedComplete(Entity<SurgeryClampBleedEffectComponent> ent, ref SurgeryStepEvent args)
    {
    }
    private void OnStepOrganInsertComplete(Entity<SurgeryStepOrganInsertComponent> ent, ref SurgeryStepEvent args)
    {
        if (!TryGetStepTool(args.StepProto, args.Tools, out var organId)
            || !TryComp<BodyPartComponent>(args.Part, out var bodyPart))
        {
            args.IsCancelled = true;
            return;
        }

        var containerId = SharedBodySystem.GetOrganContainerId(ent.Comp.Slot);

        // Frontier species do not all declare Starlight's optional surgical slots
        // in their body prototypes. Create the requested slot on demand so every
        // compatible race can receive implants and cavity items.
        if (!_body.CanInsertOrgan(args.Part, ent.Comp.Slot, bodyPart)
            && !_body.TryCreateOrganSlot(args.Part, ent.Comp.Slot, out _, bodyPart))
        {
            args.IsCancelled = true;
            return;
        }

        if (ent.Comp.Slot == "cavity" && _containers.TryGetContainer(args.Part, containerId, out var container))
        {
            _containers.Insert(organId, container);
            return;
        }

        if (!TryComp<OrganComponent>(organId, out _)
            || !_containers.TryGetContainer(args.Part, containerId, out var organContainer))
        {
            args.IsCancelled = true;
            return;
        }

        var part = args.Part;
        var body = args.Body;

        // Radiant sector: a stale or species-provided organ may still occupy the
        // slot. Replace it authoritatively, otherwise ContainerSlot rejects the
        // held organ and the UI appears to complete without implanting anything.
        foreach (var previous in organContainer.ContainedEntities.ToArray())
        {
            if (previous == organId)
                continue;

            if (!_containers.Remove(previous, organContainer, force: true, destination: Transform(body).Coordinates))
            {
                args.IsCancelled = true;
                return;
            }

            var extracted = new SurgeryOrganExtracted(body, part, previous);
            RaiseLocalEvent(previous, ref extracted);
        }

        if (!_containers.Insert(organId, organContainer, force: true))
        {
            args.IsCancelled = true;
            return;
        }

        var ev = new SurgeryOrganImplantationCompleted(body, part, organId);
        RaiseLocalEvent(organId, ref ev);
    }

    // Radiant sector: adult anatomy is changed only by completed, server-authoritative surgery steps.
    private void OnStepAdultOrganComplete(Entity<SurgeryStepAdultOrganComponent> ent, ref SurgeryStepEvent args)
    {
        if (!TryComp<AdultAnatomyComponent>(args.Body, out var anatomy))
        {
            args.IsCancelled = true;
            return;
        }

        switch (ent.Comp.Operation)
        {
            case AdultSurgeryOperation.Insert:
            {
                // Radiant sector: never install a second genital organ through
                // stale UI state. An existing vagina or penis must be extracted first.
                if (ent.Comp.Organ == AdultOrganType.Penis
                    && (anatomy.HasPenis || anatomy.HasVagina))
                {
                    args.IsCancelled = true;
                    return;
                }

                var heldOrgan = args.Tools.FirstOrDefault(uid =>
                    TryComp<AdultOrganItemComponent>(uid, out var item) && item.Organ == ent.Comp.Organ);
                if (heldOrgan == default || !TryComp<AdultOrganItemComponent>(heldOrgan, out var organItem))
                {
                    args.IsCancelled = true;
                    return;
                }

                AdultAnatomyHelpers.SetOrgan(anatomy, ent.Comp.Organ, true);
                if (ent.Comp.Organ == AdultOrganType.Penis)
                {
                    anatomy.PenisNervesIntact = organItem.PenisNervesIntact;
                    anatomy.PenisSurgicallyRemoved = false;
                }
                if (ent.Comp.Organ == AdultOrganType.Vagina)
                    anatomy.VaginaSurgicallyRemoved = false;
                if (ent.Comp.Organ == AdultOrganType.Breasts)
                {
                    anatomy.BreastSize = organItem.BreastSize;
                    anatomy.BreastSizeSurgicallyChanged = true;
                    anatomy.BreastsSurgicallyRemoved = false;
                }
                QueueDel(heldOrgan);
                break;
            }
            case AdultSurgeryOperation.Extract:
            {
                if (!AdultAnatomyHelpers.HasOrgan(anatomy, ent.Comp.Organ))
                {
                    args.IsCancelled = true;
                    return;
                }

                var prototype = ent.Comp.Organ switch
                {
                    AdultOrganType.Penis => "AdultOrganPenis",
                    AdultOrganType.Vagina => "AdultOrganVagina",
                    AdultOrganType.Breasts => "AdultOrganBreasts",
                    _ => null,
                };
                if (prototype == null)
                {
                    args.IsCancelled = true;
                    return;
                }

                var extracted = Spawn(prototype, Transform(args.Body).Coordinates);
                if (ent.Comp.Organ == AdultOrganType.Breasts
                    && TryComp<AdultOrganItemComponent>(extracted, out var breastItem))
                {
                    breastItem.BreastSize = anatomy.BreastSize;
                    Dirty(extracted, breastItem);
                }
                else if (ent.Comp.Organ == AdultOrganType.Penis
                         && TryComp<AdultOrganItemComponent>(extracted, out var penisItem))
                {
                    penisItem.PenisNervesIntact = anatomy.PenisNervesIntact;
                    Dirty(extracted, penisItem);
                }

                AdultAnatomyHelpers.SetOrgan(anatomy, ent.Comp.Organ, false);
                if (ent.Comp.Organ == AdultOrganType.Penis)
                {
                    anatomy.PenisSurgicallyRemoved = true;
                    DropSurgicalCondom(args.Body);
                }
                if (ent.Comp.Organ == AdultOrganType.Vagina)
                    anatomy.VaginaSurgicallyRemoved = true;
                if (ent.Comp.Organ == AdultOrganType.Breasts)
                {
                    anatomy.BreastsSurgicallyRemoved = true;
                    anatomy.BreastSizeSurgicallyChanged = false;
                    RemComp<UdderComponent>(args.Body);
                }
                break;
            }
            case AdultSurgeryOperation.EnlargeBreasts:
                if (!anatomy.HasBreasts || anatomy.BreastSize == AdultBreastSize.Large)
                {
                    args.IsCancelled = true;
                    return;
                }
                anatomy.BreastSize++;
                anatomy.BreastSizeSurgicallyChanged = true;
                break;
            case AdultSurgeryOperation.ReduceBreasts:
                if (!anatomy.HasBreasts || anatomy.BreastSize == AdultBreastSize.Small)
                {
                    args.IsCancelled = true;
                    return;
                }
                anatomy.BreastSize--;
                anatomy.BreastSizeSurgicallyChanged = true;
                break;
            case AdultSurgeryOperation.RemoveLactation:
                if (!HasComp<UdderComponent>(args.Body))
                {
                    args.IsCancelled = true;
                    return;
                }
                RemComp<UdderComponent>(args.Body);
                break;
            case AdultSurgeryOperation.DenervatePenis:
                if (!anatomy.HasPenis || !anatomy.PenisNervesIntact)
                {
                    args.IsCancelled = true;
                    return;
                }
                anatomy.PenisNervesIntact = false;
                break;
        }

        anatomy.SurgicallyModified = true;
        Dirty(args.Body, anatomy);
    }

    /// <summary>Radiant sector: extracting a penis must not silently delete its condom.</summary>
    private void DropSurgicalCondom(EntityUid body)
    {
        if (!TryComp<CondomWornComponent>(body, out var condom))
            return;

        var prototype = condom.Used
            ? condom.ItemPrototype switch
            {
                "PinkCondom" => "UsedPinkCondom",
                "TealCondom" => "UsedTealCondom",
                _ => "UsedCondom",
            }
            : condom.ItemPrototype;

        Spawn(prototype, Transform(body).Coordinates);
        RemComp<CondomWornComponent>(body);
    }

    // Use the item required by the current step, rather than whichever held item
    // happens to be enumerated first.
    private bool TryGetStepTool(EntProtoId stepProto, List<EntityUid> tools, out EntityUid tool)
    {
        tool = default;
        if (!_entity.TryGetSingleton(stepProto, out var step)
            || !TryComp<SurgeryStepComponent>(step, out var stepComp))
            return false;

        foreach (var required in (stepComp.Tools ?? []).Values)
        {
            var component = required.Component.GetType();
            var candidate = tools.FirstOrDefault(uid => HasComp(uid, component));
            if (candidate == default)
                continue;

            tool = candidate;
            return true;
        }

        return false;
    }

    private void OnStepOrganExtractComplete(Entity<SurgeryStepOrganExtractComponent> ent, ref SurgeryStepEvent args)
    {
        // Radiant sector: never mark an extraction step complete unless an
        // actual organ/implant was removed from the selected body part.
        args.IsCancelled = true;
        if (ent.Comp.Organ?.Count != 1)
            return;

        var type = ent.Comp.Organ.Values.First().Component.GetType();

        var destination = Transform(args.Body).Coordinates;
        if (ent.Comp.Slot != null && _containers.TryGetContainer(args.Part, SharedBodySystem.GetOrganContainerId(ent.Comp.Slot), out var container))
        {
            foreach (var containedEnt in container.ContainedEntities.ToArray())
                if (HasComp(containedEnt, type))
                {
                    if (!_containers.Remove(containedEnt, container, force: true, destination: destination))
                        return;

                    var ev = new SurgeryOrganExtracted(args.Body, args.Part, containedEnt);
                    RaiseLocalEvent(containedEnt, ref ev);
                    args.IsCancelled = false;
                    return;
                }

            return;
        }

        var organs = _body.GetPartOrgans(args.Part, Comp<BodyPartComponent>(args.Part));
        foreach (var organ in organs)
        {
            if (!HasComp(organ.Id, type)
                || !_containers.TryGetContainingContainer((organ.Id, null, null), out var organContainer)
                || !_containers.Remove(organ.Id, organContainer, force: true, destination: destination))
                continue;

            var ev = new SurgeryOrganExtracted(args.Body, args.Part, organ.Id);
            RaiseLocalEvent(organ.Id, ref ev);
            args.IsCancelled = false;

            return;
        }
    }

    private void OnRemoveAccent(Entity<SurgeryRemoveAccentComponent> ent, ref SurgeryStepEvent args)
    {
        foreach (var accent in _accents)
            if (HasComp(args.Body, accent))
                RemCompDeferred(args.Body, accent);
    }

    private void OnStepEmoteEffectComplete(Entity<SurgeryStepEmoteEffectComponent> ent, ref SurgeryStepEvent args)
    {

        if (!HasComp<PainNumbnessComponent>(args.Body) && !HasComp<SleepingComponent>(args.Body))
            _chat.TryEmoteWithChat(args.Body, ent.Comp.Emote);
        else
            _sleeping.TryWaking(args.Body); // If the patient sleeping without n2o or reagents, wake them up.
    }

    private void OnStepSpawnComplete(Entity<SurgeryStepSpawnEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (TryComp(args.Body, out TransformComponent? xform))
            SpawnAtPosition(ent.Comp.Entity, xform.Coordinates);
    }

    private void OnStepAttachLimbComplete(Entity<SurgeryStepAttachLimbEffectComponent> _, string slot, ref SurgeryStepEvent args)
    {
        // Radiant sector: select the held limb matching the operated slot. The old
        // FirstOrDefault check could choose a tool or the other arm and cancel the step.
        if (!TryGetHeldLimbForSlot(args.Tools, slot, out var limbId, out var limb)
            || !TryComp(args.Part, out BodyPartComponent? part)
            || !TryResolveBodySlot(part, slot, out var actualSlot)
            || !_body.AttachPart(args.Part, actualSlot, limbId, part, limb))
            args.IsCancelled = true;
    }

    // Radiant sector: accept both Starlight's spaced slot IDs and Frontier's
    // underscored IDs, but always pass the real prototype slot to BodySystem.
    private static bool TryResolveBodySlot(BodyPartComponent part, string requestedSlot, out string actualSlot)
    {
        var requested = requestedSlot.Replace('_', ' ').ToLowerInvariant();
        foreach (var slot in part.Children.Keys)
        {
            if (slot.Replace('_', ' ').ToLowerInvariant() != requested)
                continue;

            actualSlot = slot;
            return true;
        }

        actualSlot = string.Empty;
        return false;
    }

    private bool TryGetHeldLimbForSlot(
        List<EntityUid> held,
        string slot,
        out EntityUid limbId,
        out BodyPartComponent limb)
    {
        limbId = default;
        limb = default!;

        var normalized = slot.Replace("_", " ").ToLowerInvariant();
        var expectedType = normalized switch
        {
            "head" => BodyPartType.Head,
            "left arm" or "right arm" => BodyPartType.Arm,
            "left hand" or "right hand" => BodyPartType.Hand,
            "left leg" or "right leg" => BodyPartType.Leg,
            "left foot" or "right foot" => BodyPartType.Foot,
            "tail" => BodyPartType.Tail,
            _ => BodyPartType.Other,
        };
        var expectedSymmetry = normalized.StartsWith("left ")
            ? BodyPartSymmetry.Left
            : normalized.StartsWith("right ")
                ? BodyPartSymmetry.Right
                : BodyPartSymmetry.None;

        foreach (var candidate in held)
        {
            if (!TryComp<BodyPartComponent>(candidate, out var candidatePart)
                || candidatePart.PartType != expectedType
                || expectedSymmetry != BodyPartSymmetry.None && candidatePart.Symmetry != expectedSymmetry)
                continue;

            limbId = candidate;
            limb = candidatePart;
            return true;
        }

        return false;
    }

    private void OnStepAttachItemComplete(Entity<SurgeryStepAttachLimbEffectComponent> _, string slot, ref SurgeryStepEvent args)
        => args.IsCancelled = true;

    private void OnStepAmputationComplete(Entity<SurgeryStepAmputationEffectComponent> ent, ref SurgeryStepEvent args)
    {
        if (_containers.TryGetContainingContainer((args.Part, null, null), out var container))
        {
            // Some species define their own part prototypes and removal guards.
            // Surgical amputation is authoritative and must detach every real body part.
            var destination = Transform(args.Body).Coordinates;
            args.IsCancelled = !_containers.Remove(args.Part, container, force: true, destination: destination);
        }
        else
        {
            args.IsCancelled = true;
        }
    }

    private void CustomLimbRemoved(Entity<CustomLimbMarkerComponent> ent, ref ComponentRemove args)
    {
        if (ent.Comp.VirtualPart is null) return;
        QueueDel(ent.Comp.VirtualPart.Value);
    }
}

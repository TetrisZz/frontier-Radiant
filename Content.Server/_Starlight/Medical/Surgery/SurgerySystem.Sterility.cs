using System.Linq;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Clothing.Components;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Placeable;
using Content.Shared.Chemistry.Components;

namespace Content.Server._Starlight.Medical.Surgery;

public sealed partial class SurgerySystem
{
    [Dependency] private SharedSolutionContainerSystem _sterilitySolutions = default!;
    [Dependency] private SharedDoAfterSystem _sterilityDoAfter = default!;
    [Dependency] private PlaceableSurfaceSystem _sterilitySurfaces = default!;

    private void InitializeSterility()
    {
        SubscribeLocalEvent<SurgeryToolComponent, DroppedEvent>(OnSterilityDropped);
        SubscribeLocalEvent<OrganComponent, DroppedEvent>(OnSterilityDropped);
        SubscribeLocalEvent<BodyPartComponent, DroppedEvent>(OnSterilityDropped);
        SubscribeLocalEvent<SurgicalProtectionComponent, DroppedEvent>(OnSterilityDropped);
        SubscribeLocalEvent<SurgicalItemSterilityComponent, ExaminedEvent>(OnSterilityExamined);
        SubscribeLocalEvent<DrainableSolutionComponent, AfterInteractEvent>(OnAntisepticInteract, before: new[] { typeof(SolutionTransferSystem) });
        SubscribeLocalEvent<DrainableSolutionComponent, SurgicalDisinfectionDoAfterEvent>(OnDisinfectionFinished);
        SubscribeLocalEvent<OrganComponent, SurgeryOrganExtracted>(OnSterilityExtracted);
        SubscribeLocalEvent<SurgeryTargetComponent, BodyPartRemovedEvent>(OnSterilityAmputated);
        SubscribeLocalEvent<OrganComponent, MapInitEvent>(OnSterilityItemInit);
        SubscribeLocalEvent<BodyPartComponent, MapInitEvent>(OnSterilityItemInit);
        SubscribeLocalEvent<SterileSurgicalStorageComponent, ExaminedEvent>(OnSterileStorageExamined);
    }

    private void OnSterileStorageExamined(Entity<SterileSurgicalStorageComponent> ent, ref ExaminedEvent args)
        => args.PushMarkup(Loc.GetString("surgical-storage-sterile"));

    private bool IsInSterileStorage(EntityUid item)
    {
        var visited = new HashSet<EntityUid>();
        while (visited.Add(item) && _containers.TryGetContainingContainer(item, out var container))
        {
            item = container.Owner;
            if (HasComp<SterileSurgicalStorageComponent>(item))
                return true;
        }
        return false;
    }

    private void OnSterilityItemInit<T>(Entity<T> ent, ref MapInitEvent args) where T : Component
        => EnsureComp<SurgicalItemSterilityComponent>(ent);

    private void OnSterilityAmputated(Entity<SurgeryTargetComponent> ent, ref BodyPartRemovedEvent args)
    {
        if (TerminatingOrDeleted(args.Part.Owner) || TerminatingOrDeleted(ent))
            return;
        var state = EnsureComp<SurgicalItemSterilityComponent>(args.Part.Owner);
        state.Used = true;
        state.LastPatient = ent;
        Dirty(args.Part.Owner, state);
        if (TryComp<SurgicalSterilityComponent>(args.Part.Owner, out var field))
        {
            field.Draped.Clear();
            Dirty(args.Part.Owner, field);
        }
    }

    private void OnSterilityDropped<T>(Entity<T> ent, ref DroppedEvent args) where T : Component
    {
        if (HasComp<MaskComponent>(ent))
            return;
        if (IsInSterileStorage(ent))
            return;
        // Preserve the current cleanliness; placing an already dirty tool does not wash it.
        if ((HasComp<SurgeryToolComponent>(ent) || HasComp<OrganComponent>(ent) || HasComp<BodyPartComponent>(ent))
            && _sterilitySurfaces.IsPlacingItem(ent))
            return;
        var state = EnsureComp<SurgicalItemSterilityComponent>(ent);
        if (HasComp<SurgicalAntisepticComponent>(ent) || HasComp<NonContactSurgicalToolComponent>(ent))
        {
            SurgicalSterilityRules.Disinfect(state);
            Dirty(ent, state);
            return;
        }
        state.Dirty = true;
        Dirty(ent, state);
    }

    private void OnSterilityExtracted(Entity<OrganComponent> ent, ref SurgeryOrganExtracted args)
    {
        var state = EnsureComp<SurgicalItemSterilityComponent>(ent);
        state.Used = true;
        state.LastPatient = args.Body;
        Dirty(ent, state);
    }

    private void OnSterilityExamined(Entity<SurgicalItemSterilityComponent> ent, ref ExaminedEvent args)
    {
        if (HasComp<MaskComponent>(ent) || HasComp<SurgicalAntisepticComponent>(ent) || HasComp<NonContactSurgicalToolComponent>(ent))
            return;
        args.PushMarkup(Loc.GetString(ent.Comp.Dirty ? "surgical-item-dirty"
            : ent.Comp.Used ? "surgical-item-used" : "surgical-item-clean"));
        if (ent.Comp.ContainerContamination > 0)
            args.PushMarkup(Loc.GetString("surgical-warning-container"));
    }

    protected override void ApplySurgicalContamination(EntityUid surgery, EntityUid step,
        ref SurgeryStepCompleteEvent args, HashSet<EntityUid> usedTools, List<EntityUid> heldBefore)
    {
        if (TerminatingOrDeleted(args.Part))
            return;
        // Different physiology will receive its own rules; no biological contamination for IPCs.
        if (!UsesSurgicalSterility(args.Body, args.Part))
            return;

        var site = GetSurgicalSite(surgery, step);
        var containerPenalty = usedTools.Any(item => HasComp<DrainableSolutionComponent>(item)
            && !HasComp<SurgicalAntisepticComponent>(item) && !HasComp<SurgicalLavageComponent>(item))
            ? SurgicalSolutionRules.OrdinaryContainerContamination : 0;
        if (TryComp<SurgerySiteTreatmentComponent>(step, out var treatment))
        {
            var state = EnsureComp<SurgicalSterilityComponent>(args.Part);
            switch (treatment.Treatment)
            {
                case SurgicalTreatment.Drape:
                    foreach (var item in usedTools)
                    {
                        if (!HasComp<SurgicalDrapeComponent>(item))
                            continue;
                        state.Draped.Add(site);
                        QueueDel(item);
                        break;
                    }
                    break;
                case SurgicalTreatment.RemoveDrape:
                    state.Draped.Remove(site);
                    break;
                case SurgicalTreatment.Lavage:
                    state.Contamination[site] = containerPenalty;
                    state.Infection[site] = SurgicalInfectionRules.AfterLavage(state.Infection.GetValueOrDefault(site));
                    state.RecoveryCredit[site] = 0;
                    state.Lavaged.Add(site);
                    break;
                case SurgicalTreatment.Debride:
                    EntityManager.System<SurgicalNecrosisSystem>().Debride(args.Part, site);
                    state.Contamination[site] = containerPenalty;
                    state.Infection[site] = SurgicalInfectionRules.AfterLavage(state.Infection.GetValueOrDefault(site));
                    break;
                case SurgicalTreatment.RestoreHeart:
                    EntityManager.System<SurgicalNecrosisSystem>().RestoreHeart(args.Part);
                    break;
            }
            Dirty(args.Part, state);
            return;
        }
        if (HasComp<SurgeryDisinfectionComponent>(step))
        {
            var cleaned = EnsureComp<SurgicalSterilityComponent>(args.Part);
            cleaned.Contamination[site] = containerPenalty;
            Dirty(args.Part, cleaned);
            return;
        }

        // Insertion effects move the actual organ/limb out of the hand. Other held items
        // must not contribute, and a failed insertion never reaches this callback.
        foreach (var item in heldBefore)
        {
            if (TerminatingOrDeleted(item))
                continue;
            if (TryComp<OrganComponent>(item, out var organ) && organ.Body == args.Body
                || TryComp<BodyPartComponent>(item, out var limb) && limb.Body == args.Body)
                usedTools.Add(item);
        }
        if (usedTools.Count == 0)
            return; // bookkeeping steps are not another invasive contact

        var wound = EnsureComp<SurgicalSterilityComponent>(args.Part);
        var current = wound.Contamination.GetValueOrDefault(site, SurgicalSterilityRules.UntreatedSite);
        // Washing reduces the local contamination, but infected tissue can still soil an instrument.
        var exposure = SurgicalInfectionRules.Stage(wound.Infection.GetValueOrDefault(site)) > 0
            ? Math.Max(current, SurgicalSterilityRules.ContaminatedSite) : current;
        var added = 0;
        foreach (var tool in usedTools)
        {
            var transferred = Contact(tool, args.Body, exposure);
            added += transferred;
            if (transferred > 0 && heldBefore.Contains(tool)
                && TryComp<OrganComponent>(tool, out var inserted) && inserted.Body == args.Body)
                ContaminateImplantOrgan(tool, args.Part, transferred);
        }

        var goodGloves = false;
        if (_inventorySystem.TryGetSlotEntity(args.User, "gloves", out var gloves)
            && HasComp<SurgicalProtectionComponent>(gloves))
        {
            var gloveState = EnsureComp<SurgicalItemSterilityComponent>(gloves.Value);
            goodGloves = !gloveState.Dirty && (!gloveState.Used || gloveState.LastPatient == args.Body);
            added += Contact(gloves.Value, args.Body, exposure);
        }

        var goodMask = _inventorySystem.TryGetSlotEntity(args.User, "mask", out var mask)
            && HasComp<SurgicalProtectionComponent>(mask)
            && !(TryComp<MaskComponent>(mask, out var maskState) && maskState.IsToggled);
        var badOuterwear = _inventorySystem.TryGetSlotEntity(args.User, "outerClothing", out var outerwear)
            && !HasComp<SurgicalOuterwearComponent>(outerwear);
        added += SurgicalSterilityRules.EquipmentRisk(goodGloves, goodMask, badOuterwear);
        added += EnvironmentRisk(args.Body);

        added = SurgicalSterilityRules.ApplyFieldProtection(added, wound.Draped.Contains(site));
        if (HasComp<SurgeryClearProgressComponent>(step)
            || TryComp<SurgeryStepCavityEffectComponent>(step, out var closing) && !closing.Open)
            wound.Draped.Remove(site);

        SurgicalSterilityRules.AddContamination(wound, site, added);
        Dirty(args.Part, wound);
    }

    /// <summary>Transfers contamination to the implantation target after a successful insertion.</summary>
    public void ContaminateImplantOrgan(EntityUid implant, EntityUid part, int amount)
    {
        if (amount <= 0 || !TryComp<BodyPartComponent>(part, out var anatomy)
            || anatomy.Body is not { } patient || !UsesSurgicalSterility(patient, part))
            return;
        var dirtyImplant = TryComp<SurgicalItemSterilityComponent>(implant, out var itemState) && itemState.Dirty
            && !IsSurgicalSlime(patient);
        if (HasComp<HandImplantComponent>(implant) && TryComp<BodyPartComponent>(part, out var hand)
            && hand.PartType == BodyPartType.Hand)
        {
            var tissue = EnsureComp<SurgicalSterilityComponent>(part);
            if (dirtyImplant)
                tissue.NecrosisSeconds.TryAdd(SurgicalSite.Surface, 0);
            Dirty(part, tissue);
        }
        foreach (var organ in _body.GetPartOrgans(part))
        {
            var target = HasComp<HeartImplantComponent>(implant) && HasComp<OrganHeartComponent>(organ.Id)
                || HasComp<BrainImplantComponent>(implant) && HasComp<OrganBrainComponent>(organ.Id)
                || HasComp<EyeImplantComponent>(implant) && HasComp<OrganEyesComponent>(organ.Id);
            if (!target || HasComp<CyberneticOrganComponent>(organ.Id))
                continue;
            var state = EnsureComp<SurgicalSterilityComponent>(organ.Id);
            SurgicalSterilityRules.AddContamination(state, SurgicalSite.Surface, amount);
            if (dirtyImplant)
                EnsureComp<SurgicalOrganNecrosisComponent>(organ.Id);
            Dirty(organ.Id, state);
        }
    }

    private int Contact(EntityUid item, EntityUid patient, int contamination)
    {
        if (HasComp<SurgicalAntisepticComponent>(item) || HasComp<NonContactSurgicalToolComponent>(item))
            return 0;
        var state = EnsureComp<SurgicalItemSterilityComponent>(item);
        var gloves = HasComp<SurgicalProtectionComponent>(item) && !HasComp<MaskComponent>(item);
        var transferred = SurgicalSterilityRules.Contact(state, patient, contamination,
            HasComp<SurgeryToolComponent>(item) || gloves,
            gloves ? SurgicalSterilityRules.GloveUsesBeforeDirty : SurgicalSterilityRules.ToolUsesBeforeDirty);
        Dirty(item, state);
        return transferred;
    }

    protected override void WarnSurgicalRisk(EntityUid user, EntityUid body, EntityUid part, EntityUid step, HashSet<EntityUid> tools)
    {
        if (!UsesSurgicalSterility(body, part) || tools.Count == 0)
            return;
        var warnings = new List<string>();
        if (HasComp<SurgerySiteTreatmentComponent>(step) || HasComp<SurgeryDisinfectionComponent>(step))
        {
            if (tools.Any(item => HasComp<DrainableSolutionComponent>(item)
                && !HasComp<SurgicalAntisepticComponent>(item) && !HasComp<SurgicalLavageComponent>(item)))
                _popup.PopupEntity(Loc.GetString("surgical-warning-container"), user, user, PopupType.MediumCaution);
            return;
        }
        foreach (var item in tools)
        {
            if (HasComp<NonContactSurgicalToolComponent>(item) || HasComp<SurgicalAntisepticComponent>(item))
                continue;
            if (TryComp<SurgicalItemSterilityComponent>(item, out var state)
                && (state.Dirty || state.Used && state.LastPatient != body))
            {
                warnings.Add(Loc.GetString("surgical-warning-tool"));
                break;
            }
        }
        var glovesOk = _inventorySystem.TryGetSlotEntity(user, "gloves", out var gloves)
            && HasComp<SurgicalProtectionComponent>(gloves)
            && !(TryComp<SurgicalItemSterilityComponent>(gloves, out var gs) && (gs.Dirty || gs.Used && gs.LastPatient != body));
        var maskOk = _inventorySystem.TryGetSlotEntity(user, "mask", out var mask)
            && HasComp<SurgicalProtectionComponent>(mask)
            && !(TryComp<MaskComponent>(mask, out var ms) && ms.IsToggled);
        var badCoat = _inventorySystem.TryGetSlotEntity(user, "outerClothing", out var coat) && !HasComp<SurgicalOuterwearComponent>(coat);
        if (!glovesOk || !maskOk || badCoat)
            warnings.Add(Loc.GetString("surgical-warning-clothing"));
        if (warnings.Count > 0)
            _popup.PopupEntity(string.Join("\n", warnings), user, user, PopupType.MediumCaution);
    }

    private bool CanDisinfectItem(EntityUid item)
        => !HasComp<MaskComponent>(item)
            && (HasComp<SurgeryToolComponent>(item) || HasComp<SurgicalProtectionComponent>(item)
            || HasComp<OrganComponent>(item) || HasComp<BodyPartComponent>(item))
            && !(TryComp<OrganComponent>(item, out var organ) && organ.Body != null)
            && !(TryComp<BodyPartComponent>(item, out var part) && part.Body != null);

    private bool HasAntiseptic(EntityUid item) => HasPureSurgicalSolution(item, "Ethanol", 5);

    private void OnAntisepticInteract(Entity<DrainableSolutionComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;
        if (HasComp<RefillableSolutionComponent>(target)
            && !HasComp<OrganComponent>(target) && !HasComp<BodyPartComponent>(target))
            return;
        if (!CanDisinfectItem(target))
        {
            if (HasComp<SurgeryTargetComponent>(target)
                && (HasAntiseptic(ent) || HasPureSurgicalSolution(ent, "Saline", 10)))
            {
                args.Handled = true;
                if (args.User == target)
                {
                    _popup.PopupEntity(Loc.GetString("starlight-surgery-popup-self"), args.User, args.User);
                    return;
                }
                _ui.OpenUi(target, SurgeryUIKey.Key, args.User);
                RefreshUI(target);
            }
            return;
        }
        // Leave unrelated chemical transfers alone.
        if (!HasComp<SurgicalAntisepticComponent>(ent)
            && (!_sterilitySolutions.TryGetDrainableSolution(ent.Owner, out _, out var liquid)
                || liquid.GetTotalPrototypeQuantity("Ethanol") <= 0))
            return;
        args.Handled = true;
        if (!HasAntiseptic(ent))
        {
            _popup.PopupEntity(Loc.GetString("surgical-solution-pure-required",
                ("reagent", Loc.GetString("reagent-name-ethanol")), ("amount", 5)), args.User, args.User);
            return;
        }
        _sterilityDoAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, 3,
            new SurgicalDisinfectionDoAfterEvent(), ent, target: target, used: ent)
        {
            NeedHand = true,
            BreakOnMove = true,
            BreakOnDamage = true,
            DistanceThreshold = 1.5f,
        });
    }

    private void OnDisinfectionFinished(Entity<DrainableSolutionComponent> ent, ref SurgicalDisinfectionDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target
            || !CanDisinfectItem(target) || !HasAntiseptic(ent)
            || !_sterilitySolutions.TryGetDrainableSolution(ent.Owner, out var solution, out _))
            return;
        args.Handled = true;
        _sterilitySolutions.RemoveReagent(solution.Value, new ReagentQuantity("Ethanol", 5));
        var state = EnsureComp<SurgicalItemSterilityComponent>(target);
        SurgicalSterilityRules.Disinfect(state);
        if (!HasComp<SurgicalAntisepticComponent>(ent) && !HasComp<SurgicalLavageComponent>(ent))
            state.ContainerContamination = SurgicalSolutionRules.OrdinaryContainerContamination;
        Dirty(target, state);
        _popup.PopupEntity(Loc.GetString("surgical-antiseptic-done"), args.User, args.User);
    }
}

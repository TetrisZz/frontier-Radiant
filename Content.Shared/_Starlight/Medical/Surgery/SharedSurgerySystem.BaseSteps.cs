using Content.Shared._Starlight.Medical.Body.Part;
using Content.Shared.Body.Part;
using BodyPartType = Content.Shared.Body.Part.BodyPartType;
using Content.Shared.Body.Systems;
using Content.Shared.Buckle.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Popups;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using System.Linq;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._radiant.ERP;

namespace Content.Shared._Starlight.Medical.Surgery;
// Based on the RMC14.
// https://github.com/RMC-14/RMC-14
public abstract partial class SharedSurgerySystem
{
    [Dependency] private IRobustRandom _random = default!;

    private void InitializeSteps()
    {
        SubscribeLocalEvent<SurgeryStepComponent, SurgeryStepCompleteEvent>(OnStepComplete);
        SubscribeLocalEvent<SurgeryClearProgressComponent, SurgeryStepCompleteEvent>(OnClearProgressStep);
        SubscribeLocalEvent<SurgeryStepComponent, SurgeryStepEvent>(OnStep);
        SubscribeLocalEvent<SurgeryTargetComponent, SurgeryDoAfterEvent>(OnTargetDoAfter);
        SubscribeLocalEvent<SurgeryTargetComponent, AccessibleOverrideEvent>(OnOverrideAccess);

        SubscribeLocalEvent<SurgeryStepComponent, SurgeryCanPerformStepEvent>(OnCanPerformStep);
        SubscribeLocalEvent<SurgeryStepAttachLimbEffectComponent, SurgeryCanPerformStepEvent>(OnStepAttachCanPerform);
        SubscribeLocalEvent<SurgeryStepAdultOrganComponent, SurgeryCanPerformStepEvent>(OnAdultOrganStepCanPerform);

        Subs.BuiEvents<SurgeryTargetComponent>(SurgeryUIKey.Key, subs => subs.Event<SurgeryStepChosenBuiMsg>(OnSurgeryTargetStepChosen));
    }
    private void OnTargetDoAfter(Entity<SurgeryTargetComponent> ent, ref SurgeryDoAfterEvent args)
    {
        if (args.Cancelled ||
            args.Handled ||
            args.Target is not { } target ||
            !IsSurgeryValid(ent, target, args.Surgery, args.Step, out var surgery, out var part, out var step) ||
            !PreviousStepsComplete(ent, part, surgery, args.Step) ||
            !CanPerformStep(args.User, ent, part.Comp.PartType, step, false))
        {
            Log.Warning($"{ToPrettyString(args.User)} tried to start invalid surgery.");
            Dirty(ent);
            if (args.Target.HasValue && TryComp<BodyPartComponent>(args.Target.Value, out var dirtyPart))
                Dirty(args.Target.Value, dirtyPart, Comp<MetaDataComponent>(args.Target.Value));
            RefreshUI(ent);
            return;
        }

        if (!_random.Prob(args.SuccessRate))
        {
            if (_net.IsClient) return;
            _popup.PopupEntity(Loc.GetString("starlight-surgery-popup-failed-tool"), args.User, PopupType.SmallCaution);
            RefreshUI(ent);
            return;
        }

        var ev = new SurgeryStepEvent(args.User, ent, part, GetTools(args.User))
        {
            StepProto = args.Step,
            SurgeryProto = args.Surgery,
        };
        RaiseLocalEvent(step, ref ev);

        if (ev.IsCancelled)
        {
            RefreshUI(ent);
            return;
        }
        var evComplete = new SurgeryStepCompleteEvent(args.User, ent, part, GetTools(args.User))
        {
            StepProto = args.Step,
            SurgeryProto = args.Surgery,
            IsFinal = surgery.Comp.Steps[^1] == args.Step,
        };
        RaiseLocalEvent(step, ref evComplete);

        RefreshUI(ent);
    }

    private void OnClearProgressStep(Entity<SurgeryClearProgressComponent> ent, ref SurgeryStepCompleteEvent args)
    {
        var progress = Comp<SurgeryProgressComponent>(args.Part);
        // Radiant sector: closing an incision resets the whole surgical state.
        // Leaving StartedSurgeries behind made attachment operations reappear at
        // step one while bypassing their occupied-slot condition.
        progress.CompletedSteps.Clear();
        progress.CompletedSurgeries.Clear();
        progress.StartedSurgeries.Clear();
        Dirty(args.Part, progress);
    }
    private void OnStepComplete(Entity<SurgeryStepComponent> ent, ref SurgeryStepCompleteEvent args)
    {
        if (TryComp<SurgeryClearProgressComponent>(ent, out _)) return;
        if (TryComp<SurgeryProgressComponent>(args.Part, out var progress))
        {
            progress.CompletedSteps.Add($"{args.SurgeryProto}:{args.StepProto}");
            if (!progress.StartedSurgeries.Contains(args.SurgeryProto) && !args.IsFinal)
                progress.StartedSurgeries.Add(args.SurgeryProto);
            if (progress.StartedSurgeries.Contains(args.SurgeryProto) && args.IsFinal)
                progress.StartedSurgeries.Remove(args.SurgeryProto);
        }
        else
        {
            progress = new SurgeryProgressComponent { CompletedSteps = [$"{args.SurgeryProto}:{args.StepProto}"]};
            if(!args.IsFinal)
                progress.StartedSurgeries.Add(args.SurgeryProto);
            AddComp(args.Part, progress);
        }
        if (!args.IsFinal)
            return;

        // Radiant sector: attaching a limb changes the socket condition and must be
        // repeatable after that limb is amputated later. Keeping it in the completed
        // set made the second attachment appear completed and therefore unusable.
        if (_entitySystem.TryGetSingleton(args.SurgeryProto, out var surgeryEntity)
            && (HasComp<SurgeryLimbSlotConditionComponent>(surgeryEntity)
                || HasComp<SurgeryAdultOrganConditionComponent>(surgeryEntity)
                || HasComp<SurgeryAdultBreastSizeConditionComponent>(surgeryEntity)
                || HasComp<SurgeryCavityConditionComponent>(surgeryEntity)))
        {
            if (TryComp<SurgeryComponent>(surgeryEntity, out var surgery))
            {
                foreach (var step in surgery.Steps)
                    progress.CompletedSteps.Remove($"{args.SurgeryProto}:{step}");
            }

            progress.CompletedSurgeries.Remove(args.SurgeryProto);
            Dirty(args.Part, progress);
            return;
        }

        progress.CompletedSurgeries.Add(args.SurgeryProto);
    }
    private void OnStep(Entity<SurgeryStepComponent> ent, ref SurgeryStepEvent args)
    {
        if(!_entitySystem.TryGetSingleton(args.StepProto, out var stepEnt)
            || !TryComp(stepEnt, out SurgeryStepComponent? stepComp)) return;

        foreach (var reg in (ent.Comp.Tools ?? []).Values)
        {
            var tool = args.Tools.FirstOrDefault(x => HasComp(x, reg.Component.GetType()));
            if (tool == default) return;

            var specificToolComp = EntityManager.GetComponents(tool)
                .OfType<ISurgeryToolComponent>();

            SoundSpecifier? endSound = null;
            foreach(var usedTool in specificToolComp)
            {
                var requestedTool = stepComp.Tools?.FirstOrDefault().Key;
                if(requestedTool != null)
                    if(usedTool.ToolType.Contains(requestedTool))
                    {
                        endSound = usedTool.EndSound;
                    }
            }

            if (_net.IsServer && TryComp(tool, out SurgeryToolComponent? toolComp) && endSound != null)
                _audio.PlayPvs(endSound, tool);

            if (ent.Comp.ReagentId != null && _solutionContainerSystem.TryGetSolution(tool, "drink", out var solution))
                _solutionContainerSystem.RemoveReagent(solution.Value, new ReagentQuantity(ent.Comp.ReagentId, ent.Comp.ReagentQuantity));
        }

        foreach (var reg in (ent.Comp.Add ?? []).Values)
        {
            var compType = reg.Component.GetType();
            if (HasComp(args.Part, compType))
                continue;
            var newComp = _compFactory.GetComponent(compType);
            _serialization.CopyTo(reg.Component, ref newComp, notNullableOverride: true);
            AddComp(args.Part, newComp);
        }

        if (ent.Comp.BodyAdd != null)
            EntityManager.AddComponents(args.Body, ent.Comp.BodyAdd, false);

        foreach (var reg in (ent.Comp.Remove ?? []).Values)
            RemComp(args.Part, reg.Component.GetType());

        foreach (var reg in (ent.Comp.BodyRemove ?? []).Values)
            RemComp(args.Body, reg.Component.GetType());
    }

    private void OnOverrideAccess(Entity<SurgeryTargetComponent> ent, ref AccessibleOverrideEvent args)
    {
        // Check if the entity is the target to avoid giving the hooked entity access to everything.
        // If we already have access we don't need to run more code.
        if (args.Accessible || args.Target != ent.Owner)
            return;

        var xform = Transform(ent);
        var root = _containers.GetContainingContainers((ent, xform)).FirstOrDefault(x => x.ID == SharedBodySystem.BodyRootContainerId); //get the root container
        if (root == null)
            return;
        if (!_interaction.InRangeUnobstructed(args.User, root.Owner))
            return;

        args.Accessible = true;
        args.Handled = true;
    }

    private void OnCanPerformStep(Entity<SurgeryStepComponent> ent, ref SurgeryCanPerformStepEvent args)
    {
        if (HasComp<SurgeryOperatingTableConditionComponent>(ent)
            && (!TryComp(args.Body, out BuckleComponent? buckle) || !HasComp<OperatingTableComponent>(buckle.BuckledTo)))
        {
            args.Invalid = StepInvalidReason.NeedsOperatingTable;
            return;
        }

        RaiseLocalEvent(args.Body, ref args);

        if (args.Invalid != StepInvalidReason.None)
            return;

        if (_inventory.TryGetContainerSlotEnumerator(args.Body, out var enumerator, args.TargetSlots))
        {
            var items = 0f;
            var total = 0f;
            while (enumerator.MoveNext(out var con))
            {
                total++;
                if (con.ContainedEntity != null && !_tag.HasTag(con.ContainedEntity.Value, "SurgeryCompatibleArmor"))
                    items++;
            }

            if (items > 0)
            {
                args.Invalid = StepInvalidReason.Armor;
                args.Popup = Loc.GetString("starlight-surgery-popup-remove-armor");
                return;
            }
        }

        if (args.Invalid != StepInvalidReason.None || ent.Comp.Tools == null)
            return;

        foreach (var reg in ent.Comp.Tools.Values)
        {
            var tool = args.Tools.FirstOrDefault(x => HasComp(x, reg.Component.GetType()));
            if (tool == default)
            {
                args.Invalid = StepInvalidReason.MissingTool;

                if (reg.Component is ISurgeryToolComponent toolComp)
                    // Radiant sector: ToolName stores a locale key so surgery hints follow the client's language.
                    args.Popup = Loc.GetString("starlight-surgery-popup-need-tool", ("tool", Loc.GetString(toolComp.ToolName)));

                return;
            }
            else if (TryComp<ItemToggleComponent>(tool, out var togglable) && !togglable.Activated)
            {
                args.Invalid = StepInvalidReason.DisabledTool;

                if (reg.Component is ISurgeryToolComponent toolComp)
                    args.Popup = Loc.GetString("starlight-surgery-popup-enable-tool", ("tool", Loc.GetString(toolComp.ToolName)));

                return;
            }
            else if (TryComp<SurgeryItemSizeConditionComponent>(ent, out var itemSizeComp) && TryComp<ItemComponent>(tool, out var item) && _item.GetSizePrototype(item.Size) > _item.GetSizePrototype(itemSizeComp.Size))
            {
                args.Invalid = StepInvalidReason.TooHigh;
                return;
            }
            else if (ent.Comp.ReagentId != null && _solutionContainerSystem.GetTotalPrototypeQuantity(tool, ent.Comp.ReagentId) < ent.Comp.ReagentQuantity)
            {
                args.Invalid = StepInvalidReason.NotEnoughReagent;
                if (reg.Component is ISurgeryToolComponent toolComp)
                    args.Popup = Loc.GetString("starlight-surgery-popup-need-reagent", ("amount", ent.Comp.ReagentQuantity), ("reagent", ent.Comp.ReagentId), ("tool", Loc.GetString(toolComp.ToolName)));
                return;
            }

            args.ValidTools.Add(tool);
        }
    }

    // block blacklisted items, the UI for surgury can act a bit strange so it does a little double check and lets the player know.
    private void OnStepAttachCanPerform(Entity<SurgeryStepAttachLimbEffectComponent> ent, ref SurgeryCanPerformStepEvent args)
    {
        // Radiant sector: hand enumeration order must not hide a valid limb when
        // the surgeon holds an ordinary instrument in the other hand.
        if (args.Tools.Any(itemId => HasComp<BodyPartComponent>(itemId)))
            return;

        args.Invalid = StepInvalidReason.MissingLimb;
        args.Popup = Loc.GetString("starlight-surgery-popup-missing-limb");
    }

    private void OnAdultOrganStepCanPerform(Entity<SurgeryStepAdultOrganComponent> ent, ref SurgeryCanPerformStepEvent args)
    {
        if (ent.Comp.Operation != AdultSurgeryOperation.Insert)
            return;

        if (args.Tools.Any(uid => TryComp<AdultOrganItemComponent>(uid, out var item) && item.Organ == ent.Comp.Organ))
            return;

        args.Invalid = StepInvalidReason.MissingTool;
        args.Popup = Loc.GetString("starlight-surgery-popup-missing-adult-organ");
    }

    private void OnSurgeryTargetStepChosen(Entity<SurgeryTargetComponent> ent, ref SurgeryStepChosenBuiMsg args)
    {
        var user = args.Actor;
        if (GetEntity(args.Entity) is not { Valid: true } body
            || GetEntity(args.Part) is not { Valid: true } targetPart
            || !IsSurgeryValid(body, targetPart, args.Surgery, args.Step, out var surgery, out var part, out var step)
            // Radiant sector: ERP refusal applies to both participants. Client
            // filtering is only presentation; this is the authoritative guard.
            || IsAdultSurgery(surgery) && (IsErpDenied(user) || IsErpDenied(body))
            || !_entitySystem.TryGetSingleton(args.Step, out var stepEnt)
            || !TryComp(stepEnt, out SurgeryStepComponent? stepComp)
            || !CanPerformStep(user, body, part.Comp.PartType, step, true, out _, out _, out var validTools))
        {
            return;
        }
        if (!PreviousStepsComplete(body, part, surgery, args.Step) || IsStepComplete(part, args.Surgery, args.Step))
        {
            var progress = Comp<SurgeryProgressComponent>(part);
            Dirty(part, progress);
            RefreshUI(ent);
            return;
        }

        if (_net.IsServer && TryComp(step, out MetaDataComponent? meta))
        {
            var surgeonName = MetaData(user).EntityName;
            _popup.PopupEntity(Loc.GetString("starlight-surgery-popup-starts", ("surgeon", surgeonName), ("step", meta.EntityName)), part, PopupType.LargeCaution);
        }

        var duration = stepComp.Duration;
        float SmallestSuccessRate = 1f;

        foreach (var tool in validTools)
            if (TryComp(tool, out SurgeryToolComponent? toolComp))
            {
                var toolSpeed = 1f;
                var toolSuccessRate = 1f;
                SoundSpecifier? startSound = null;
                var specificToolComp = EntityManager.GetComponents(tool)
                    .OfType<ISurgeryToolComponent>();

                foreach(var usedTool in specificToolComp)
                {
                    var requestedTool = stepComp.Tools?.FirstOrDefault().Key;
                    if(requestedTool != null)
                        if(usedTool.ToolType.Contains(requestedTool))
                        {
                            toolSpeed = usedTool.Speed;
                            toolSuccessRate = usedTool.SuccessRate;
                            startSound = usedTool.StartSound;
                        }
                }

                duration *= toolSpeed;
                if (startSound != null) _audio.PlayPvs(startSound, tool);

                if(toolSuccessRate < SmallestSuccessRate)
                    SmallestSuccessRate = toolSuccessRate;
            }

        if (TryComp(body, out TransformComponent? xform))
            _rotateToFace.TryFaceCoordinates(user, _transform.GetMapCoordinates(body, xform).Position);

        var ev = new SurgeryDoAfterEvent(args.Surgery, args.Step, SmallestSuccessRate);
        var doAfter = new DoAfterArgs(EntityManager, user, duration, ev, body, part)
        {
            BreakOnMove = true,
            NeedHand = validTools.Count > 0,
            // Radiant sector: tool-free steps such as "Locate Implant" must not
            // reference EntityUid.Invalid as their used entity. DoAfter treats
            // that as a real (but missing) entity and silently cancels the step.
            Used = validTools.Count > 0 ? validTools.First() : null,
            // Repeated UI clicks must not cancel the operation already in progress.
            BlockDuplicate = true,
            CancelDuplicate = false,
            DuplicateCondition = DuplicateConditions.SameTarget,
        };
        _doAfter.TryStartDoAfter(doAfter);
    }

    public (Entity<SurgeryComponent> Surgery, int Step)? GetNextStep(EntityUid body, EntityUid part, EntityUid surgery) => GetNextStep(body, part, surgery, []);
    private (Entity<SurgeryComponent> Surgery, int Step)? GetNextStep(EntityUid body, EntityUid part, Entity<SurgeryComponent?> surgery, List<EntityUid> requirements)
    {
        if (!Resolve(surgery, ref surgery.Comp))
            return null;

        if (requirements.Contains(surgery))
            throw new ArgumentException($"Surgery {surgery} has a requirement loop: {string.Join(", ", requirements)}");

        requirements.Add(surgery);

        if (surgery.Comp.Requirement is { } requirementsIds)
        {
            foreach (var requirementId in requirementsIds)
            {
                var requirementProgress = CompOrNull<SurgeryProgressComponent>(part);
                if (IsSurgeryRequirementSatisfied(part, requirementProgress, requirementId))
                    continue;

                // Radiant sector: recurse only when the prerequisite prototype
                // exists. The inverted check made attachment buttons unable to
                // start their required incision surgery.
                if (_entitySystem.TryGetSingleton(requirementId, out var requirement)
                    && GetNextStep(body, part, requirement, requirements) is { } requiredNext
                    && IsSurgeryValid(body, part, requirementId, requiredNext.Surgery.Comp.Steps[requiredNext.Step], out _, out _, out _))
                    return requiredNext;
            }
        }

        if (!TryComp<SurgeryProgressComponent>(part, out var progress))
        {
            AddComp<SurgeryProgressComponent>(part);
            return ((surgery, surgery.Comp), 0);
        }
        var surgeryProto = Prototype(surgery);
        for (var i = 0; i < surgery.Comp.Steps.Count; i++)
            if (!progress.CompletedSteps.Contains($"{surgeryProto?.ID}:{surgery.Comp.Steps[i]}"))
                return ((surgery, surgery.Comp), i);

        return null;
    }

    public bool PreviousStepsComplete(EntityUid body, EntityUid part, Entity<SurgeryComponent> surgery, EntProtoId step)
    {
        if (surgery.Comp.Requirement is { } requirements)
        {
            var progress = CompOrNull<SurgeryProgressComponent>(part);
            foreach (var requirement in requirements)
            {
                if (IsSurgeryRequirementSatisfied(part, progress, requirement))
                    continue;

                if (!_entitySystem.TryGetSingleton(requirement, out var requiredEnt)
                    || !TryComp(requiredEnt, out SurgeryComponent? requiredComp)
                    || !PreviousStepsComplete(body, part, (requiredEnt, requiredComp), step)
                    && IsSurgeryValid(body, part, requirement, step, out _, out _, out _))
                    return false;
            }
        }

        foreach (var surgeryStep in surgery.Comp.Steps)
        {
            if (surgeryStep == step)
                break;

            if (Prototype(surgery.Owner) is not EntityPrototype surgProto || !IsStepComplete(part, surgProto.ID, surgeryStep))
                return false;
        }

        return true;
    }

    public bool CanPerformStep(EntityUid user, EntityUid body, BodyPartType part, EntityUid step, bool doPopup) => CanPerformStep(user, body, part, step, doPopup, out _, out _, out _);
    public bool CanPerformStep(EntityUid user, EntityUid body, BodyPartType part, EntityUid step, bool doPopup, out string? popup, out StepInvalidReason reason, out HashSet<EntityUid> validTools)
    {
        var slot = part switch
        {
            BodyPartType.Head => SlotFlags.HEAD | SlotFlags.MASK | SlotFlags.EYES,
            BodyPartType.Torso => SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING,
            BodyPartType.Arm => SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING,
            BodyPartType.Hand => SlotFlags.GLOVES,
            BodyPartType.Leg => SlotFlags.OUTERCLOTHING | SlotFlags.INNERCLOTHING,
            BodyPartType.Foot => SlotFlags.FEET,
            BodyPartType.Tail => SlotFlags.NONE,
            BodyPartType.Other => SlotFlags.NONE,
            _ => SlotFlags.NONE
        };

        var check = new SurgeryCanPerformStepEvent(user, body, GetTools(user), slot);
        RaiseLocalEvent(step, ref check);
        popup = check.Popup;
        validTools = check.ValidTools;

        if (check.Invalid != StepInvalidReason.None)
        {
            if (doPopup && check.Popup != null)
                _popup.PopupEntity(check.Popup, user, PopupType.SmallCaution);

            reason = check.Invalid;
            return false;
        }

        reason = default;
        return true;
    }

    public bool IsStepComplete(EntityUid part, EntProtoId surgery, EntProtoId step)
    {
        if (TryComp<SurgeryProgressComponent>(part, out var comp))
            return comp.CompletedSteps.Contains($"{surgery}:{step}");
        AddComp<SurgeryProgressComponent>(part);
        return false;
    }
}

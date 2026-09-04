using Content.Shared._Starlight.Medical.Limbs;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Hands.Components;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Medical.Limbs;

// Radiant sector start - Frontier body-event compatibility
public sealed partial class CyberLimbSystem
{
    public void InitializeToggleable()
    {
        SubscribeLocalEvent<BodyComponent, BodyPartAddedEvent>(OnBodyPartAdded);
        SubscribeLocalEvent<BodyComponent, BodyPartRemovedEvent>(OnBodyPartRemoved);
    }

    private void OnBodyPartAdded(Entity<BodyComponent> body, ref BodyPartAddedEvent args)
    {
        // Radiant sector: a preassembled cyberarm is attached as one part, so
        // Frontier does not raise a separate BodyPartAddedEvent for its hand.
        // The deploy action lives on that nested hand, not on the arm itself.
        // BodyPartAddedEvent is raised before RecursiveBodyUpdate has finished.
        // Defer action creation so the nested hand and its metadata are already
        // present on clients before ActionComponent references its container.
        var bodyId = body.Owner;
        var partId = args.Part.Owner;
        Timer.Spawn(TimeSpan.Zero, () =>
        {
            if (TerminatingOrDeleted(bodyId)
                || TerminatingOrDeleted(partId)
                || !TryComp<BodyPartComponent>(partId, out var part)
                || part.Body != bodyId)
                return;

            AddPartActions(bodyId, partId);

            foreach (var (slot, child) in GetDirectChildParts((partId, part)))
            {
                AddPartActions(bodyId, child);
                AddNestedHand(bodyId, slot, child);
            }
        });
    }

    private void AddPartActions(EntityUid body, EntityUid part)
    {
        // Radiant sector: old saves may contain an ID of an action whose entity
        // was already removed. Clear it before asking ActionsSystem to reuse it.
        if (TryComp<LimbItemDeployerComponent>(part, out var staleDeployer)
            && staleDeployer.ActionEntity is { } staleDeployerAction
            && TerminatingOrDeleted(staleDeployerAction))
        {
            staleDeployer.ActionEntity = null;
            Dirty(part, staleDeployer);
        }

        if (TryComp<LimbItemDeployerComponent>(part, out var deployer))
        {
            var actionEntity = deployer.ActionEntity;
            _actions.AddAction(body, ref actionEntity, deployer.Action, part);
            deployer.ActionEntity = actionEntity;
            Dirty(part, deployer);
        }

        if (TryComp<LimbWithActionComponent>(part, out var staleAction)
            && staleAction.ActionEntity is { } staleActionEntity
            && TerminatingOrDeleted(staleActionEntity))
        {
            staleAction.ActionEntity = null;
            Dirty(part, staleAction);
        }

        if (TryComp<LimbWithActionComponent>(part, out var action))
        {
            var actionEntity = action.ActionEntity;
            _actions.AddAction(body, ref actionEntity, action.Action, part);
            action.ActionEntity = actionEntity;
            Dirty(part, action);
        }
    }

    private void OnBodyPartRemoved(Entity<BodyComponent> body, ref BodyPartRemovedEvent args)
    {
        RemovePartActions(body.Owner, args.Part.Owner);

        foreach (var (slot, child) in GetDirectChildParts(args.Part))
        {
            RemovePartActions(body.Owner, child);
            RemoveNestedHand(body.Owner, slot, child);
        }
    }

    private void RemovePartActions(EntityUid body, EntityUid part)
    {
        if (TryComp<LimbItemDeployerComponent>(part, out var deployer))
        {
            if (deployer.Toggled)
            {
                var toggle = new ToggleLimbEvent { Performer = body };
                OnLimbToggle((part, deployer), ref toggle);
            }

            _actions.RemoveAction(body, deployer.ActionEntity);
            // Keep the action in the limb's ActionsContainer. It can then be
            // granted again safely if the same detached limb is reinstalled.
            if (deployer.ActionEntity is { } actionEntity && TerminatingOrDeleted(actionEntity))
                deployer.ActionEntity = null;
            Dirty(part, deployer);
        }

        if (TryComp<LimbWithActionComponent>(part, out var action))
        {
            _actions.RemoveAction(body, action.ActionEntity);
            if (action.ActionEntity is { } actionEntity && TerminatingOrDeleted(actionEntity))
                action.ActionEntity = null;
            Dirty(part, action);
        }
    }

    // Radiant sector: Frontier only raises the body event for the outer arm.
    // Keep its preassembled hand's inventory slot synchronized explicitly.
    private void AddNestedHand(EntityUid body, string slot, EntityUid child)
    {
        // Radiant sector: an outer arm may contain either an organic or a cyber
        // hand. Both kinds must restore their inventory slot with the arm.
        if (!TryComp<BodyPartComponent>(child, out var childPart)
            || childPart.PartType != BodyPartType.Hand
            || !TryComp<HandsComponent>(body, out var hands))
            return;

        var handId = SharedBodySystem.GetPartSlotContainerId(slot);
        if (_hands.TryGetHand((body, hands), handId, out _))
            return;

        var location = childPart.Symmetry switch
        {
            BodyPartSymmetry.Left => HandLocation.Left,
            BodyPartSymmetry.Right => HandLocation.Right,
            _ => HandLocation.Middle,
        };

        _hands.AddHand((body, hands), handId, location);
    }

    private void RemoveNestedHand(EntityUid body, string slot, EntityUid child)
    {
        // Radiant sector: removing an entire organic arm must remove the nested
        // hand slot too, just like cutting the hand off on its own does.
        if (TryComp<BodyPartComponent>(child, out var childPart)
            && childPart.PartType == BodyPartType.Hand)
        {
            _hands.RemoveHand(body, SharedBodySystem.GetPartSlotContainerId(slot));
        }
    }

    private IEnumerable<(string Slot, EntityUid Part)> GetDirectChildParts(Entity<BodyPartComponent> part)
    {
        foreach (var slot in part.Comp.Children.Keys)
        {
            var containerId = SharedBodySystem.GetPartSlotContainerId(slot);
            if (!_container.TryGetContainer(part.Owner, containerId, out var container))
                continue;

            foreach (var child in container.ContainedEntities)
            {
                if (HasComp<BodyPartComponent>(child))
                    yield return (slot, child);
            }
        }
    }
}
// Radiant sector end

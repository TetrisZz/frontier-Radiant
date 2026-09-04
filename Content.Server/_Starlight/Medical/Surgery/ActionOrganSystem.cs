using Content.Shared.Actions;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery.Events;

namespace Content.Server._Starlight.Medical.Surgery;

/// <summary>
/// Keeps actions supplied by surgically installed organs attached to their body.
/// </summary>
public sealed class ActionOrganSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActionOrganComponent, SurgeryOrganImplantationCompleted>(OnImplanted);
        SubscribeLocalEvent<ActionOrganComponent, SurgeryOrganExtracted>(OnExtracted);
        SubscribeLocalEvent<ActionOrganComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnImplanted(Entity<ActionOrganComponent> ent, ref SurgeryOrganImplantationCompleted args)
    {
        // Radiant sector: Starlight's original action-organ system is unavailable on
        // this branch. The implant owns the action, while the patient performs it.
        if (ent.Comp.ActionEntity is { } existing && !TerminatingOrDeleted(existing))
            _actions.RemoveAction(existing);

        ent.Comp.ActionEntity = null;
        _actions.AddAction(args.Body, ref ent.Comp.ActionEntity, ent.Comp.Action, ent.Owner);
        Dirty(ent);
    }

    private void OnExtracted(Entity<ActionOrganComponent> ent, ref SurgeryOrganExtracted args)
    {
        RemoveAction(ent);
    }

    private void OnShutdown(Entity<ActionOrganComponent> ent, ref ComponentShutdown args)
    {
        RemoveAction(ent);
    }

    private void RemoveAction(Entity<ActionOrganComponent> ent)
    {
        if (ent.Comp.ActionEntity is { } action)
            _actions.RemoveAction(action);

        ent.Comp.ActionEntity = null;
        Dirty(ent);
    }
}

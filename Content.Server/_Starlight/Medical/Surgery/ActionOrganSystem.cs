using Content.Shared.Actions;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Server.Emp;
using Content.Server._Starlight.Medical.Surgery.Components;

namespace Content.Server._Starlight.Medical.Surgery;

/// <summary>
/// Keeps actions supplied by surgically installed organs attached to their body.
/// </summary>
public sealed partial class ActionOrganSystem : EntitySystem
{
    [Dependency] private SharedActionsSystem _actions = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ActionOrganComponent, SurgeryOrganImplantationCompleted>(OnImplanted);
        SubscribeLocalEvent<ActionOrganComponent, SurgeryOrganExtracted>(OnExtracted);
        SubscribeLocalEvent<ActionOrganComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ActionOrganComponent, EmpPulseEvent>(OnEmpPulse);
        SubscribeLocalEvent<ActionOrganComponent, EmpDisabledRemoved>(OnEmpDisabledRemoved);
    }

    private void OnImplanted(Entity<ActionOrganComponent> ent, ref SurgeryOrganImplantationCompleted args)
    {
        // Radiant sector: Starlight's original action-organ system is unavailable on
        // this branch. The implant owns the action, while the patient performs it.
        if (ent.Comp.ActionEntity is { } existing && !TerminatingOrDeleted(existing))
            _actions.RemoveAction(existing);

        ent.Comp.ActionEntity = null;
        var empState = EnsureComp<EmpDisabledActionOrganComponent>(ent);
        empState.Body = args.Body;
        empState.Disabled = false;
        _actions.AddAction(args.Body, ref ent.Comp.ActionEntity, ent.Comp.Action, ent.Owner);
        Dirty(ent);
    }

    private void OnExtracted(Entity<ActionOrganComponent> ent, ref SurgeryOrganExtracted args)
    {
        RemoveAction(ent);
        RemComp<EmpDisabledActionOrganComponent>(ent);
    }

    private void OnShutdown(Entity<ActionOrganComponent> ent, ref ComponentShutdown args)
    {
        RemoveAction(ent);
    }

    private void OnEmpPulse(Entity<ActionOrganComponent> ent, ref EmpPulseEvent args)
    {
        if (!TryComp<EmpDisabledActionOrganComponent>(ent, out var state))
            return;

        args.Affected = true;
        args.Disabled = true;
        if (state.Disabled)
            return;

        state.Disabled = true;
        RemoveAction(ent);
    }

    private void OnEmpDisabledRemoved(Entity<ActionOrganComponent> ent, ref EmpDisabledRemoved args)
    {
        if (!TryComp<EmpDisabledActionOrganComponent>(ent, out var state) || !state.Disabled)
            return;

        ent.Comp.ActionEntity = null;
        _actions.AddAction(state.Body, ref ent.Comp.ActionEntity, ent.Comp.Action, ent.Owner);
        state.Disabled = false;
        Dirty(ent);
    }

    private void RemoveAction(Entity<ActionOrganComponent> ent)
    {
        if (ent.Comp.ActionEntity is { } action)
            _actions.RemoveAction(action);

        ent.Comp.ActionEntity = null;
        Dirty(ent);
    }
}

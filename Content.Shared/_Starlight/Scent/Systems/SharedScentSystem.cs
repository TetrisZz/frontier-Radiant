using Content.Shared._Starlight.Scent.Components;
using Content.Shared.Actions;
using Content.Shared.Eye;

namespace Content.Shared._Starlight.Scent.Systems;

/// <summary>
/// Radiant-compatible subset of Starlight's scent controls.
/// </summary>
public abstract class SharedScentSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedEyeSystem _eye = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SmellerComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<SmellerComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<SmellerComponent, ToggleSniffActionEvent>(OnToggle);
        SubscribeLocalEvent<SmellerComponent, ClearTrackedScentActionEvent>(OnClear);
    }

    private void OnInit(Entity<SmellerComponent> ent, ref ComponentInit args)
    {
        _actions.AddAction(ent.Owner, ref ent.Comp.ToggleActionEntity, ent.Comp.ToggleAction);
    }

    private void OnShutdown(Entity<SmellerComponent> ent, ref ComponentShutdown args)
    {
        _actions.RemoveAction(ent.Owner, ent.Comp.ToggleActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.TrackActionEntity);
        _actions.RemoveAction(ent.Owner, ent.Comp.ClearActionEntity);
        _eye.RefreshVisibilityMask(ent.Owner);
    }

    private void OnToggle(Entity<SmellerComponent> ent, ref ToggleSniffActionEvent args)
    {
        if (args.Handled)
            return;

        SetSniffing(ent, !ent.Comp.Sniffing);
        args.Handled = true;
    }

    private void OnClear(Entity<SmellerComponent> ent, ref ClearTrackedScentActionEvent args)
    {
        if (args.Handled)
            return;

        SetTrackedScent(ent, null);
        args.Handled = true;
    }

    public void SetSniffing(Entity<SmellerComponent> ent, bool sniffing)
    {
        ent.Comp.Sniffing = sniffing;
        _actions.SetToggled(ent.Comp.ToggleActionEntity, sniffing);

        if (sniffing)
            _actions.AddAction(ent.Owner, ref ent.Comp.TrackActionEntity, ent.Comp.TrackAction);
        else
            _actions.RemoveAction(ent.Owner, ent.Comp.TrackActionEntity);

        _eye.RefreshVisibilityMask(ent.Owner);
        Dirty(ent);
    }

    public void SetTrackedScent(Entity<SmellerComponent> ent, string? scentId)
    {
        ent.Comp.TrackedScentId = scentId;

        if (scentId != null)
            _actions.AddAction(ent.Owner, ref ent.Comp.ClearActionEntity, ent.Comp.ClearAction);
        else
            _actions.RemoveAction(ent.Owner, ent.Comp.ClearActionEntity);

        Dirty(ent);
    }
}

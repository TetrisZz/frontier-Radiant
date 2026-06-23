using Content.Shared._radiant.Abilities.Arcana;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Humanoid;
using Content.Shared.Popups;
using Robust.Shared.GameObjects;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Abilities.Arcana;

public sealed class ArcanaAuraAbilitySystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<ArcanaAuraAbilityComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ArcanaAuraAbilityComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ArcanaAuraAbilityComponent, ArcanaAuraToggleEvent>(OnToggle);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<ArcanaAuraAbilityComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (!component.Enabled || curTime < component.NextPulse)
                continue;

            PulseAura(uid, component);
            component.NextPulse = curTime + component.PulseInterval;
        }
    }

    private void OnMapInit(Entity<ArcanaAuraAbilityComponent> entity, ref MapInitEvent args)
    {
        if (!TryComp(entity, out ActionsComponent? actions))
            return;

        _actions.AddAction(entity, ref entity.Comp.ActionEntity, entity.Comp.Action, component: actions);
        _actions.SetToggled(entity.Comp.ActionEntity, entity.Comp.Enabled);
    }

    private void OnShutdown(Entity<ArcanaAuraAbilityComponent> entity, ref ComponentShutdown args)
    {
        SetAuraEnabled(entity, false, null, false);
        _actions.RemoveAction(entity.Owner, entity.Comp.ActionEntity);
    }

    private void OnToggle(Entity<ArcanaAuraAbilityComponent> entity, ref ArcanaAuraToggleEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        SetAuraEnabled(entity, !entity.Comp.Enabled, args.Performer, true);
    }

    private void SetAuraEnabled(
        Entity<ArcanaAuraAbilityComponent> entity,
        bool enabled,
        EntityUid? performer,
        bool showPopup)
    {
        entity.Comp.Enabled = enabled;
        entity.Comp.NextPulse = _timing.CurTime + entity.Comp.PulseInterval;
        Dirty(entity);

        _actions.SetToggled(entity.Comp.ActionEntity, enabled);

        if (!showPopup || performer == null)
            return;

        var message = enabled ? entity.Comp.EnabledPopup : entity.Comp.DisabledPopup;
        _popup.PopupEntity(Loc.GetString(message), entity.Owner, performer.Value);

        if (enabled)
            PulseAura(entity.Owner, entity.Comp);
    }

    private void PulseAura(EntityUid uid, ArcanaAuraAbilityComponent component)
    {
        if (component.AuraMessages.Count == 0)
            return;

        var message = Loc.GetString(_random.Pick(component.AuraMessages));

        var coordinates = Transform(uid).Coordinates;
        foreach (var (recipient, _) in _lookup.GetEntitiesInRange<HumanoidAppearanceComponent>(coordinates, component.Radius))
        {
            if (recipient == uid)
                continue;

            _popup.PopupEntity(message, recipient, recipient);
        }
    }
}

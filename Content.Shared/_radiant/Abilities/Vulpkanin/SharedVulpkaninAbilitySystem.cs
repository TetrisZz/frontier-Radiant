using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared.Cloning.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Shared._radiant.Abilities.Vulpkanin;

public sealed class SharedVulpkaninAbilitySystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<VulpkaninFeralAbilityComponent, MapInitEvent>(OnFeralInit);
        SubscribeLocalEvent<VulpkaninFeralAbilityComponent, ComponentShutdown>(OnFeralShutdown);
        SubscribeLocalEvent<VulpkaninFeralAbilityComponent, VulpkaninFeralEvent>(OnFeralActivate);
        SubscribeLocalEvent<VulpkaninFeralAbilityComponent, CloningEvent>(OnFeralClone);

        SubscribeLocalEvent<VulpkaninFeralActiveComponent, GetMeleeDamageEvent>(OnGetMeleeDamage);
        SubscribeLocalEvent<VulpkaninFeralActiveComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
        SubscribeLocalEvent<VulpkaninFeralActiveComponent, ComponentRemove>(OnFeralActiveRemove);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var curTime = _timing.CurTime;
        var query = EntityQueryEnumerator<VulpkaninFeralActiveComponent>();
        while (query.MoveNext(out var uid, out var active))
        {
            if (curTime < active.EndTime)
                continue;

            RemCompDeferred<VulpkaninFeralActiveComponent>(uid);
        }
    }

    private void OnFeralInit(Entity<VulpkaninFeralAbilityComponent> entity, ref MapInitEvent args)
    {
        if (!TryComp(entity, out ActionsComponent? actions))
            return;

        _actions.AddAction(entity, ref entity.Comp.ActionEntity, entity.Comp.Action, component: actions);
    }

    private void OnFeralShutdown(Entity<VulpkaninFeralAbilityComponent> entity, ref ComponentShutdown args)
    {
        _actions.RemoveAction(entity.Owner, entity.Comp.ActionEntity);
    }

    private void OnFeralActivate(Entity<VulpkaninFeralAbilityComponent> entity, ref VulpkaninFeralEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (HasComp<VulpkaninFeralActiveComponent>(entity))
        {
            _popup.PopupClient(Loc.GetString(entity.Comp.AlreadyActivePopup), entity, args.Performer);
            return;
        }

        var active = EnsureComp<VulpkaninFeralActiveComponent>(entity);
        active.EndTime = _timing.CurTime + entity.Comp.Duration;
        active.SpeedModifier = entity.Comp.SpeedModifier;
        active.BonusDamage = new(entity.Comp.BonusDamage);
        Dirty(entity, active);

        _movement.RefreshMovementSpeedModifiers(entity);

        if (entity.Comp.ActivateSound != null)
            _audio.PlayPredicted(entity.Comp.ActivateSound, entity, args.Performer);
    }

    private void OnGetMeleeDamage(Entity<VulpkaninFeralActiveComponent> entity, ref GetMeleeDamageEvent args)
    {
        args.Damage += entity.Comp.BonusDamage;
    }

    private void OnRefreshSpeed(Entity<VulpkaninFeralActiveComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        args.ModifySpeed(entity.Comp.SpeedModifier);
    }

    private void OnFeralActiveRemove(Entity<VulpkaninFeralActiveComponent> entity, ref ComponentRemove args)
    {
        _movement.RefreshMovementSpeedModifiers(entity);
    }

    private void OnFeralClone(Entity<VulpkaninFeralAbilityComponent> ent, ref CloningEvent args)
    {
        if (!args.Settings.EventComponents.Contains(Factory.GetRegistration(ent.Comp.GetType()).Name))
            return;

        var targetComp = Factory.GetComponent<VulpkaninFeralAbilityComponent>();
        targetComp.Action = ent.Comp.Action;
        targetComp.Duration = ent.Comp.Duration;
        targetComp.SpeedModifier = ent.Comp.SpeedModifier;
        targetComp.BonusDamage = new(ent.Comp.BonusDamage);
        targetComp.ActivateSound = ent.Comp.ActivateSound;
        targetComp.AlreadyActivePopup = ent.Comp.AlreadyActivePopup;
        AddComp(args.CloneUid, targetComp, true);
    }
}

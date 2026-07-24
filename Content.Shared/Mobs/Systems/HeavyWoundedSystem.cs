using Content.Shared.Damage;
using Content.Shared.Gravity;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mobs.Components;
using Content.Shared.Movement.Systems;
using Content.Shared.Standing;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Wieldable;
using Content.Shared.Wieldable.Components;

namespace Content.Shared.Mobs.Systems;

/// <summary>
/// Implements the conscious, crawling state immediately before critical condition.
/// </summary>
public sealed class HeavyWoundedSystem : EntitySystem
{
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly StandingStateSystem _standing = default!;
    [Dependency] private readonly SharedWieldableSystem _wieldable = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<HeavyWoundedComponent, DamageChangedEvent>(OnDamageChanged);
        SubscribeLocalEvent<HeavyWoundedComponent, WeightlessnessChangedEvent>(OnWeightlessnessChanged);
        SubscribeLocalEvent<HeavyWoundedComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<HeavyWoundedComponent, MobStateChangedEvent>(OnMobStateChanged);
        SubscribeLocalEvent<HeavyWoundedComponent, UpdateMobStateEvent>(OnUpdateMobState, after: [typeof(MobThresholdSystem)]);
        SubscribeLocalEvent<HeavyWoundedComponent, StandAttemptEvent>(OnStandAttempt);
        SubscribeLocalEvent<HeavyWoundedComponent, WieldAttemptEvent>(OnWieldAttempt);
        SubscribeLocalEvent<HeavyWoundedComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
        SubscribeLocalEvent<HeavyWoundedComponent, GetMeleeAttackRateEvent>(OnGetMeleeAttackRate);
    }

    private void OnMapInit(Entity<HeavyWoundedComponent> ent, ref MapInitEvent args)
    {
        if (TryComp<DamageableComponent>(ent, out var damageable))
            UpdateState(ent, damageable);
    }

    private void OnDamageChanged(Entity<HeavyWoundedComponent> ent, ref DamageChangedEvent args)
    {
        if (ent.Comp.RecoveredFromCritical && args.Damageable.TotalDamage > ent.Comp.DamageThreshold)
        {
            ent.Comp.RecoveredFromCritical = false;
            Dirty(ent);
        }

        UpdateState(ent, args.Damageable);
    }

    private void OnWeightlessnessChanged(Entity<HeavyWoundedComponent> ent, ref WeightlessnessChangedEvent args)
    {
        if (!ent.Comp.Active)
            return;

        // Radiant Sector: pre-critical condition remains active in space, but a weightless person cannot be forced prone.
        if (args.Weightless)
            _standing.Stand(ent, force: true);
        else
            _standing.Down(ent);
    }

    private void OnMobStateChanged(Entity<HeavyWoundedComponent> ent, ref MobStateChangedEvent args)
    {
        if (args.OldMobState == MobState.Critical && args.NewMobState == MobState.Alive)
        {
            ent.Comp.RecoveredFromCritical = true;
            Dirty(ent);
        }

        if (args.NewMobState == MobState.Critical)
        {
            ent.Comp.RecoveredFromCritical = false;
            Dirty(ent);

            // The crawling state does not drop held items, but entering actual critical condition does.
            var drop = new DropHandItemsEvent();
            RaiseLocalEvent(ent, ref drop, false);
        }

        if (TryComp<DamageableComponent>(ent, out var damageable))
            UpdateState(ent, damageable);
    }

    private void OnUpdateMobState(Entity<HeavyWoundedComponent> ent, ref UpdateMobStateEvent args)
    {
        if (args.State != MobState.Alive
            || args.Component.CurrentState != MobState.Critical
            || !TryComp<DamageableComponent>(ent, out var damageable)
            || damageable.TotalDamage <= ent.Comp.DamageThreshold)
        {
            return;
        }

        // Critical condition is only cleared after healing to 100 damage or less.
        args.State = MobState.Critical;
    }

    private void UpdateState(Entity<HeavyWoundedComponent> ent, DamageableComponent damageable)
    {
        if (!TryComp<MobStateComponent>(ent, out var mob))
            return;

        var active = damageable.TotalDamage >= ent.Comp.DamageThreshold
                     && damageable.TotalDamage < ent.Comp.CriticalThreshold
                     && !ent.Comp.RecoveredFromCritical
                     && mob.CurrentState == MobState.Alive;

        if (active == ent.Comp.Active)
            return;

        ent.Comp.Active = active;
        Dirty(ent);

        if (active)
        {
            // Reaching the crawling damage threshold causes all held items to be dropped.
            if (IsWeightless(ent))
                _standing.Stand(ent, force: true);
            else
                _standing.Down(ent);
            UnwieldHeldItems(ent);
        }
        else if (mob.CurrentState == MobState.Alive)
        {
            _standing.Stand(ent);
        }
    }

    private void UnwieldHeldItems(Entity<HeavyWoundedComponent> ent)
    {
        if (!TryComp<HandsComponent>(ent, out var hands))
            return;

        foreach (var held in _hands.EnumerateHeld((ent, hands)))
        {
            if (TryComp<WieldableComponent>(held, out var wieldable) && wieldable.Wielded)
                _wieldable.TryUnwield(held, wieldable, ent, force: true);
        }
    }

    private void OnStandAttempt(Entity<HeavyWoundedComponent> ent, ref StandAttemptEvent args)
    {
        if (ent.Comp.Active && !IsWeightless(ent))
            args.Cancel();
    }

    private bool IsWeightless(EntityUid uid) =>
        TryComp<GravityAffectedComponent>(uid, out var gravity) && gravity.Weightless;

    private void OnWieldAttempt(Entity<HeavyWoundedComponent> ent, ref WieldAttemptEvent args)
    {
        if (!ent.Comp.Active)
            return;

        args.Message = Loc.GetString("heavy-wounded-cannot-wield");
        args.Cancel();
    }

    private void OnRefreshMovementSpeed(Entity<HeavyWoundedComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Active)
            args.ModifySpeed(0.5f);
    }

    private void OnGetMeleeAttackRate(Entity<HeavyWoundedComponent> ent, ref GetMeleeAttackRateEvent args)
    {
        if (ent.Comp.Active)
            args.Multipliers *= 0.5f;
    }
}

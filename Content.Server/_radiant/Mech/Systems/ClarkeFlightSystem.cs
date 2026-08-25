using Content.Server._radiant.Mech.Components;
using Content.Server.Mech.Systems;
using Content.Server.Power.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.FixedPoint;
using Content.Shared.Gravity;
using Content.Shared.Mech.Components;
using Content.Shared.Movement.Components;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Physics.Components;
using System.Numerics;

namespace Content.Server._radiant.Mech.Systems;

/// <summary>
/// Runs the Clarke's integrated thrusters and permanent magnetic boots.
/// </summary>
public sealed class ClarkeFlightSystem : EntitySystem
{
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly SharedGravitySystem _gravity = default!;
    [Dependency] private readonly MechSystem _mech = default!;
    [Dependency] private readonly MovementSpeedModifierSystem _movementSpeed = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClarkeMagbootsComponent, IsWeightlessEvent>(OnIsWeightless);
        SubscribeLocalEvent<ClarkeFlightComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshMovementSpeed);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ClarkeFlightComponent, MechComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var flight, out var mech, out var transform))
        {
            SyncBatteryCharge(uid, mech);

            var powered = mech.Energy > 0;
            if (flight.WasPowered != powered)
            {
                flight.WasPowered = powered;
                _movementSpeed.RefreshMovementSpeedModifiers(uid);
            }

            if (mech.PilotSlot.ContainedEntity is { } pilot)
                _actionBlocker.UpdateCanMove(pilot);

            if (!powered)
            {
                RemComp<ActiveJetpackComponent>(uid);
                RemComp<CanMoveInAirComponent>(uid);
                RemComp<MovementAlwaysTouchingComponent>(uid);
                // Stop any existing drift only when the Clarke is actually out in open space.
                if (transform.GridUid == null && TryComp<PhysicsComponent>(uid, out var physics))
                    _physics.SetLinearVelocity(uid, Vector2.Zero, body: physics);
                continue;
            }

            // Restore Clarke's zero-gravity mobility as soon as a charged cell is inserted.
            EnsureComp<CanMoveInAirComponent>(uid);
            EnsureComp<MovementAlwaysTouchingComponent>(uid);

            var flying = mech.PilotSlot.ContainedEntity != null &&
                         !_gravity.EntityGridOrMapHaveGravity((uid, transform));

            if (flight.WasFlying != flying)
            {
                flight.WasFlying = flying;
                _movementSpeed.RefreshMovementSpeedModifiers(uid);
            }

            if (!flying)
            {
                RemComp<ActiveJetpackComponent>(uid);
                continue;
            }

            var energyCost = FixedPoint2.New(flight.EnergyUsePerSecond * frameTime);
            if (mech.Energy < energyCost)
            {
                // Spend the final fraction of charge rather than leaving the mech in a usable limbo state.
                if (mech.Energy > 0)
                    _mech.TryChangeEnergy(uid, -mech.Energy, mech);

                RemComp<ActiveJetpackComponent>(uid);
                RemComp<CanMoveInAirComponent>(uid);
                RemComp<MovementAlwaysTouchingComponent>(uid);
                continue;
            }

            if (!_mech.TryChangeEnergy(uid, -energyCost, mech))
            {
                RemComp<ActiveJetpackComponent>(uid);
                RemComp<CanMoveInAirComponent>(uid);
                RemComp<MovementAlwaysTouchingComponent>(uid);
                continue;
            }

            // Reuse the ordinary jetpack particle effect while Clarke is using its built-in thrusters.
            EnsureComp<ActiveJetpackComponent>(uid);
        }
    }

    private void OnRefreshMovementSpeed(Entity<ClarkeFlightComponent> entity,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!TryComp<MechComponent>(entity, out var mech))
            return;

        if (mech.Energy <= 0)
        {
            args.ModifySpeed(0.15f);
            return;
        }

        if (entity.Comp.WasFlying)
            args.ModifySpeed(entity.Comp.SpaceSpeedModifier);
    }

    private void OnIsWeightless(Entity<ClarkeMagbootsComponent> entity, ref IsWeightlessEvent args)
    {
        var transform = Transform(entity);
        if (!_gravity.EntityOnGravitySupportingGridOrMap((entity, transform)))
            return;

        args.IsWeightless = false;
        args.Handled = true;
    }

    private void SyncBatteryCharge(EntityUid uid, MechComponent mech)
    {
        var charge = FixedPoint2.Zero;
        var maxCharge = FixedPoint2.Zero;

        if (mech.BatterySlot.ContainedEntity is { } battery &&
            TryComp<BatteryComponent>(battery, out var batteryComponent))
        {
            charge = batteryComponent.CurrentCharge;
            maxCharge = batteryComponent.MaxCharge;
        }

        if (mech.Energy == charge && mech.MaxEnergy == maxCharge)
            return;

        mech.Energy = charge;
        mech.MaxEnergy = maxCharge;
        Dirty(uid, mech);
    }
}

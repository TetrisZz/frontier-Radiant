using Content.Server._radiant.Mech.Components;
using Content.Server.Gatherable.Components;
using Content.Server.Weapons.Melee;
using Content.Shared.Damage;
using Content.Shared.Damage.Systems;
using Content.Shared.Interaction;
using Content.Shared.Mech.Components;
using Content.Shared.Mech.Equipment.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Mech.Systems;

/// <summary>
/// Allows Clarke drills to mine with right-click while keeping the mech as the attack source.
/// </summary>
public sealed class ClarkeMechDrillSystem : EntitySystem
{
    private const float CursorTargetRadius = 0.75f;

    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly MeleeWeaponSystem _melee = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ClarkeMechDrillComponent, UserActivateInWorldEvent>(OnActivateInWorld);
        SubscribeLocalEvent<ClarkeMechDrillComponent, AttemptMeleeEvent>(OnAttemptMelee);
        // ПКМ у обычного бура является тяжёлой атакой, а не альтернативным взаимодействием.
        SubscribeNetworkEvent<HeavyAttackEvent>(OnHeavyAttack, before: [typeof(SharedMeleeWeaponSystem)]);
        SubscribeNetworkEvent<LightAttackEvent>(OnLightAttack, before: [typeof(SharedMeleeWeaponSystem)]);
    }

    private void OnActivateInWorld(Entity<ClarkeMechDrillComponent> drillEntity, ref UserActivateInWorldEvent args)
    {
        var drill = drillEntity.Owner;
        if (!TryComp<MechEquipmentComponent>(drill, out var equipment) || equipment.EquipmentOwner is not { } mech)
            return;

        if (args.Handled || !IsValidDrillTarget(mech, args.User, args.Target) || !TryDrill(mech, drill, args.Target))
            return;

        args.Handled = true;
    }

    private void OnAttemptMelee(Entity<ClarkeMechDrillComponent> drill, ref AttemptMeleeEvent args)
    {
        if (!TryComp<MechEquipmentComponent>(drill, out var equipment) || equipment.EquipmentOwner is not { } mech)
            return;

        if (!TryComp<MechComponent>(mech, out var mechComponent) || mechComponent.CurrentSelectedEquipment != drill)
            return;

        // ClarkeMechDrillSystem applies drill hits with the mech as the attacker.
        args.Cancelled = true;
    }

    private void OnHeavyAttack(HeavyAttackEvent args, EntitySessionEventArgs eventArgs)
    {
        if (eventArgs.SenderSession.AttachedEntity is not { } pilot ||
            !TryGetSelectedClarkeDrill(pilot, out var mech, out var drill) ||
            args.Weapon != GetNetEntity(drill))
        {
            return;
        }

        var targets = new HashSet<EntityUid>();
        CollectDrillTargets(mech, pilot, args, targets);

        foreach (var target in targets)
        {
            TryDrill(mech, drill, target);
        }

        // Prevent the stock handler from repeating the hit with the pilot as its source.
        args.Entities.Clear();
    }

    private void OnLightAttack(LightAttackEvent args, EntitySessionEventArgs eventArgs)
    {
        if (eventArgs.SenderSession.AttachedEntity is not { } pilot ||
            !TryGetSelectedClarkeDrill(pilot, out var mech, out var drill) ||
            args.Weapon != GetNetEntity(drill) ||
            args.Target == null ||
            !TryGetEntity(args.Target, out var target) ||
            target is not { } targetUid ||
            !IsValidDrillTarget(mech, pilot, targetUid))
        {
            return;
        }

        TryDrill(mech, drill, targetUid);
    }

    private void CollectDrillTargets(EntityUid mech, EntityUid pilot, HeavyAttackEvent args, HashSet<EntityUid> targets)
    {
        foreach (var netEntity in args.Entities)
        {
            if (!TryGetEntity(netEntity, out var entity) ||
                entity is not { } entityUid ||
                !IsValidDrillTarget(mech, pilot, entityUid))
                continue;

            targets.Add(entityUid);
        }

        // Mech collision causes the client's arc query to return its own chassis rather than the wall.
        // Resolve gatherable rock under the cursor like a handheld mining drill would.
        var clickCoordinates = GetCoordinates(args.Coordinates);

        foreach (var (entity, _) in _lookup.GetEntitiesInRange<GatherableComponent>(clickCoordinates, CursorTargetRadius))
        {
            if (IsValidDrillTarget(mech, pilot, entity))
                targets.Add(entity);
        }
    }

    private bool IsValidDrillTarget(EntityUid mech, EntityUid pilot, EntityUid target)
    {
        if (target == mech || target == pilot)
            return false;

        if (!TryComp<MechComponent>(mech, out var mechComponent))
            return false;

        if (mechComponent.PilotSlot.ContainedEntity == target)
            return false;

        foreach (var equipment in mechComponent.EquipmentContainer.ContainedEntities)
        {
            if (target == equipment)
                return false;
        }

        return HasComp<GatherableComponent>(target) || HasComp<DamageableComponent>(target);
    }

    private bool TryGetSelectedClarkeDrill(EntityUid user, out EntityUid mech, out EntityUid drill)
    {
        mech = default;
        drill = default;

        if (!TryComp<MechPilotComponent>(user, out var pilot) ||
            !TryComp<ClarkeFlightComponent>(pilot.Mech, out _) ||
            !TryComp<MechComponent>(pilot.Mech, out var mechComponent) ||
            mechComponent.PilotSlot.ContainedEntity != user ||
            mechComponent.CurrentSelectedEquipment is not { } selected ||
            !HasComp<ClarkeMechDrillComponent>(selected))
        {
            return false;
        }

        mech = pilot.Mech;
        drill = selected;
        return true;
    }

    private bool TryDrill(EntityUid mech, EntityUid drill, EntityUid target)
    {
        if (!TryComp<MechComponent>(mech, out var mechComponent) ||
            mechComponent.PilotSlot.ContainedEntity == null ||
            mechComponent.Energy <= 0 ||
            mechComponent.CurrentSelectedEquipment != drill ||
            !IsValidDrillTarget(mech, mechComponent.PilotSlot.ContainedEntity.Value, target) ||
            !TryComp<MeleeWeaponComponent>(drill, out var weapon) ||
            weapon.NextAttack > _timing.CurTime ||
            !_interaction.InRangeUnobstructed(mech, target, weapon.Range))
        {
            return false;
        }

        weapon.NextAttack = _timing.CurTime + TimeSpan.FromSeconds(1f / weapon.AttackRate);
        Dirty(drill, weapon);

        var damage = _melee.GetDamage(drill, mech, weapon);
        var hitEvent = new MeleeHitEvent([target], mech, drill, damage, null);
        RaiseLocalEvent(drill, hitEvent);

        // Gathering ore listens to AttackedEvent. Raising it is what makes the drill yield ore instead of merely
        // damaging the asteroid, and keeps both the tool and attacker attributed to the Clarke.
        var attackedEvent = new AttackedEvent(drill, mech, Transform(target).Coordinates);
        RaiseLocalEvent(target, attackedEvent);

        if (!Deleted(target) && HasComp<DamageableComponent>(target))
        {
            var finalDamage = DamageSpecifier.ApplyModifierSets(
                damage + hitEvent.BonusDamage + attackedEvent.BonusDamage,
                hitEvent.ModifiersList);
            _damageable.TryChangeDamage(target, finalDamage, weapon.ResistanceBypass, origin: mech);
        }

        return true;
    }
}

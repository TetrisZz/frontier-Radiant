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
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Mech.Systems;

/// <summary>
/// Allows Clarke drills to mine with right-click while keeping the mech as the attack source.
/// </summary>
public sealed partial class ClarkeMechDrillSystem : EntitySystem
{
    private const float CursorTargetRadius = 0.85f;
    private const float SwingArcCosine = 0.5f; // 120-degree mining arc.

    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private MeleeWeaponSystem _melee = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private EntityWhitelistSystem _whitelist = default!;

    private readonly SoundSpecifier _drillSound = new SoundPathSpecifier("/Audio/_radiant/Mech/clarke_drill.ogg");

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

        var hit = false;
        foreach (var target in targets)
        {
            hit |= TryDrill(mech, drill, target, hit);
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

        // The client normally raycasts from the pilot, who is inside the Clarke. That ray
        // often hits the chassis first and leaves the packet without the adjacent rock.
        // Resolve nearby rocks from the mech's position and cursor direction instead.
        if (!TryComp<MechComponent>(mech, out var mechComponent) ||
            mechComponent.CurrentSelectedEquipment is not { } drill ||
            !TryComp<MeleeWeaponComponent>(drill, out var weapon))
            return;

        var origin = _transform.GetMapCoordinates(mech);
        var aim = _transform.ToMapCoordinates(clickCoordinates);
        if (origin.MapId != aim.MapId)
            return;

        var direction = aim.Position - origin.Position;
        if (direction.LengthSquared() < 0.01f)
            return;

        direction = System.Numerics.Vector2.Normalize(direction);
        foreach (var (entity, _) in _lookup.GetEntitiesInRange<GatherableComponent>(Transform(mech).Coordinates, weapon.Range + 0.5f))
        {
            if (!IsValidDrillTarget(mech, pilot, entity))
                continue;

            var position = _transform.GetMapCoordinates(entity);
            if (position.MapId != origin.MapId)
                continue;

            var offset = position.Position - origin.Position;
            if (offset.LengthSquared() > 0.01f &&
                System.Numerics.Vector2.Dot(direction, System.Numerics.Vector2.Normalize(offset)) >= SwingArcCosine)
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

    private bool TryDrill(EntityUid mech, EntityUid drill, EntityUid target, bool sameSwing = false)
    {
        if (!TryComp<MechComponent>(mech, out var mechComponent) ||
            mechComponent.PilotSlot.ContainedEntity == null ||
            mechComponent.Energy <= 0 ||
            mechComponent.CurrentSelectedEquipment != drill ||
            !IsValidDrillTarget(mech, mechComponent.PilotSlot.ContainedEntity.Value, target) ||
            !TryComp<MeleeWeaponComponent>(drill, out var weapon) ||
            (!sameSwing && weapon.NextAttack > _timing.CurTime) ||
            (TryComp<GatherableComponent>(target, out var gatherable) &&
             _whitelist.IsWhitelistFailOrNull(gatherable.ToolWhitelist, drill)) ||
            !_interaction.InRangeUnobstructed(mech, target, weapon.Range))
        {
            return false;
        }

        if (!sameSwing)
        {
            weapon.NextAttack = _timing.CurTime + TimeSpan.FromSeconds(1f / weapon.AttackRate);
            Dirty(drill, weapon);
        }

        // Radiant Sector: the custom right-click drill route bypasses the stock melee sound handler.
        if (!sameSwing)
            _audio.PlayPvs(_drillSound, mech, AudioParams.Default.WithVolume(-6f));

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

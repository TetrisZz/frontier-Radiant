using System.Diagnostics.CodeAnalysis;
using System.Numerics;
using Content.Shared.ActionBlocker;
using Content.Shared.Buckle.Components;
using Content.Shared.Database;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Stunnable;
using Content.Shared.Throwing;
using Content.Shared.Verbs;
using Content.Shared.DoAfter;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Network;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Physics.Systems;
using Robust.Shared.Utility;

namespace Content.Shared.Weapons.Misc;

/// <summary>
/// Humanoid pulling another mob with a tether: same mouse-joint / TetherEntity approach as tether guns, but the
/// anchor stays on the puller so they drag the target while moving.
/// </summary>
public sealed class SharedPlayerLeashSystem : EntitySystem
{
    [Dependency] private readonly INetManager _net = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedJointSystem _joints = default!;
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly ThrownItemSystem _thrown = default!;
    [Dependency] private readonly MobStateSystem _mob = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedStunSystem _stun = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    private const string TetherPrototype = "TetherEntity";
    private static readonly TimeSpan PullKnockdownDuration = TimeSpan.FromSeconds(2);
    private const float YankInteractionRange = 3f;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<HumanoidAppearanceComponent, GetVerbsEvent<InteractionVerb>>(AddLeashTargetVerbs);
        SubscribeLocalEvent<PlayerLeashPullerComponent, GetVerbsEvent<InteractionVerb>>(AddSelfDetachVerb);
        SubscribeLocalEvent<PlayerLeashPullerComponent, EntityTerminatingEvent>(OnPullerTerminating);
        SubscribeLocalEvent<TetheredComponent, EntityTerminatingEvent>(OnTetheredTerminating);
        SubscribeLocalEvent<LeashComponent, PlayerLeashAttachDoAfterEvent>(OnLeashAttachDoAfter);
    }

    private void OnLeashAttachDoAfter(EntityUid leashUid, LeashComponent _, PlayerLeashAttachDoAfterEvent args)
    {
        if (!_net.IsServer || args.Handled)
            return;

        var puller = args.User;
        if (!args.Target.HasValue)
            return;

        var target = args.Target.Value;

        if (args.Cancelled)
        {
            args.Handled = true;
            return;
        }

        if (!TryGetHeldLeash(puller, out var heldItemUid, out LeashComponent? _))
        {
            _popup.PopupPredicted(Loc.GetString("player-leash-fail-no-leash"), puller, puller);
            args.Handled = true;
            return;
        }

        if (heldItemUid != leashUid)
        {
            _popup.PopupPredicted(Loc.GetString("player-leash-fail-no-leash"), puller, puller);
            args.Handled = true;
            return;
        }

        if (TryGetAttachBlocker(puller, target, out var fail))
        {
            _popup.PopupPredicted(Loc.GetString(fail!), puller, puller);
            args.Handled = true;
            return;
        }

        args.Handled = true;
        StartLeash(puller, target);
    }

    private void OnPullerTerminating(EntityUid uid, PlayerLeashPullerComponent comp, ref EntityTerminatingEvent args)
    {
        StopLeash(uid, comp);
    }

    private void OnTetheredTerminating(EntityUid uid, TetheredComponent tethered, ref EntityTerminatingEvent args)
    {
        if (!TryComp<PlayerLeashPullerComponent>(tethered.Tetherer, out var pullerComp))
            return;

        if (!HasLeashTarget(pullerComp, uid))
            return;

        StopLeashTarget(tethered.Tetherer, pullerComp, uid, land: false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var q = EntityQueryEnumerator<PlayerLeashPullerComponent, TransformComponent>();

        while (q.MoveNext(out var puller, out var leash, out var pullerXform))
        {
            if (!HasAnyLeashTarget(leash))
            {
                RemComp<PlayerLeashPullerComponent>(puller);
                continue;
            }

            if (Deleted(puller))
            {
                StopLeash(puller, leash, land: false);
                continue;
            }

            if (!TryGetHeldLeash(puller, out _, out LeashComponent? _))
            {
                PopupLeashSnapped(puller, leash);
                StopLeash(puller, leash);
                continue;
            }

            for (var slot = 0; slot < PlayerLeashPullerComponent.DefaultMaxLeashTargets; slot++)
            {
                var follower = GetFollower(leash, slot);
                var anchor = GetAnchor(leash, slot);

                if (follower is not { } followerUid || anchor is not { } anchorUid)
                    continue;

                if (Deleted(followerUid) || Deleted(anchorUid))
                {
                    if (!Deleted(followerUid))
                        PopupLeashSnapped(puller, followerUid);
                    StopLeashSlot(puller, leash, slot, land: true);
                    continue;
                }

                var mapA = Transform(puller).MapID;
                var mapB = Transform(followerUid).MapID;
                if (mapA != mapB || mapA == MapId.Nullspace)
                {
                    PopupLeashSnapped(puller, followerUid);
                    StopLeashSlot(puller, leash, slot);
                    continue;
                }

                var pPos = _transform.GetWorldPosition(pullerXform);
                var fPos = _transform.GetWorldPosition(followerUid);

                var hardLimit = leash.MaxLeashDistance;
                var distanceSquared = (pPos - fPos).LengthSquared();
                if (distanceSquared > hardLimit * hardLimit)
                {
                    PopupLeashSnapped(puller, followerUid);
                    StopLeashSlot(puller, leash, slot);
                    continue;
                }

                var currentDistance = Math.Clamp(GetCurrentDistance(leash, slot), leash.MinLeashDistance, leash.MaxLeashDistance);
                SetCurrentDistance(leash, slot, currentDistance);
                var distance = MathF.Sqrt(distanceSquared);

                if (distance <= currentDistance || distance <= 0.001f)
                    _transform.SetWorldPosition(anchorUid, fPos);
                else
                    _transform.SetWorldPosition(anchorUid, pPos + (fPos - pPos) / distance * currentDistance);
            }

            Dirty(puller, leash);
        }
    }

    private void AddSelfDetachVerb(EntityUid uid, PlayerLeashPullerComponent comp, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (args.Target != uid || args.User != uid || !HasAnyLeashTarget(comp))
            return;

        if (!args.CanInteract || !args.CanComplexInteract)
            return;

        InteractionVerb verb = new()
        {
            Text = Loc.GetString("player-leash-verb-detach"),
            Act = () => StopLeash(uid, comp),
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/unbuckle.svg.192dpi.png")),
            Priority = 20,
            Impact = LogImpact.Low,
        };

        args.Verbs.Add(verb);

        InteractionVerb tighten = new()
        {
            Text = Loc.GetString("player-leash-verb-tighten"),
            Act = () => AdjustLeashDistance(uid, comp, -comp.DistanceAdjustStep, null),
            Priority = 19,
            Impact = LogImpact.Low,
        };
        args.Verbs.Add(tighten);

        InteractionVerb loosen = new()
        {
            Text = Loc.GetString("player-leash-verb-loosen"),
            Act = () => AdjustLeashDistance(uid, comp, comp.DistanceAdjustStep, null),
            Priority = 18,
            Impact = LogImpact.Low,
        };
        args.Verbs.Add(loosen);

        InteractionVerb yank = new()
        {
            Text = Loc.GetString("player-leash-verb-yank"),
            Act = () => YankLeashTargets(uid, comp, null),
            Priority = 17,
            Impact = LogImpact.Low,
        };
        args.Verbs.Add(yank);
    }

    private void AddLeashTargetVerbs(EntityUid uid, HumanoidAppearanceComponent humanoid, ref GetVerbsEvent<InteractionVerb> args)
    {
        _ = humanoid;

        if (!HasComp<HumanoidAppearanceComponent>(args.User))
            return;

        if (args.Target == args.User)
            return;

        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        TryComp<PlayerLeashPullerComponent>(args.User, out var existing);

        if (existing != null && TryGetLeashSlot(existing, args.Target, out _))
        {
            var puller = args.User;
            var leash = existing;
            var target = args.Target;
            InteractionVerb detach = new()
            {
                Text = Loc.GetString("player-leash-verb-detach-target"),
                Act = () => StopLeashTarget(puller, leash, target),
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/unbuckle.svg.192dpi.png")),
                Priority = 20,
                Impact = LogImpact.Low,
            };
            args.Verbs.Add(detach);
            return;
        }

        if (!TryGetHeldLeash(args.User, out _, out LeashComponent? _))
            return;

        InteractionVerb attach = new()
        {
            Priority = 10,
            Impact = LogImpact.Low,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/group.svg.192dpi.png")),
            Text = Loc.GetString("player-leash-verb-attach"),
        };

        if (existing != null && ActiveLeashTargetCount(existing) >= existing.MaxLeashTargets)
        {
            attach.Disabled = true;
            attach.Message = Loc.GetString("player-leash-verb-msg-too-many");
        }
        else if (TryGetAttachBlocker(args.User, args.Target, out var locId))
        {
            attach.Disabled = true;
            attach.Message = Loc.GetString(locId);
        }

        var pullerUid = args.User;
        var targetUid = args.Target;

        attach.Act = () =>
        {
            if (!TryGetHeldLeash(pullerUid, out var leashUid, out var leashComp))
            {
                _popup.PopupPredicted(Loc.GetString("player-leash-fail-no-leash"), pullerUid, pullerUid);
                return;
            }

            if (TryGetAttachBlocker(pullerUid, targetUid, out var fail))
            {
                _popup.PopupPredicted(Loc.GetString(fail!), pullerUid, pullerUid);
                return;
            }

            var delay = TimeSpan.FromSeconds(MathF.Max(0.1f, leashComp.AttachDelaySeconds));
            var doAfterArgs = new DoAfterArgs(EntityManager, pullerUid, delay, new PlayerLeashAttachDoAfterEvent(), leashUid, targetUid, leashUid)
            {
                NeedHand = true,
                BreakOnDropItem = true,
                BreakOnMove = false,
                RequireCanInteract = true,
                BreakOnDamage = true,
                DistanceThreshold = 2.5f,
            };

            if (!_doAfter.TryStartDoAfter(doAfterArgs))
                return;

            _popup.PopupPredicted(Loc.GetString("player-leash-start-attaching"), pullerUid, pullerUid);
        };

        args.Verbs.Add(attach);
    }

    /// <summary> Returns true if interaction should be blocked (with a locale id for the reason). </summary>
    private bool TryGetAttachBlocker(EntityUid puller, EntityUid target, [NotNullWhen(true)] out string? locId)
    {
        locId = null;

        if (!_mob.IsAlive(puller) || !_mob.IsAlive(target))
        {
            locId = "player-leash-fail-not-alive";
            return true;
        }

        if (HasComp<TetheredComponent>(target))
        {
            locId = "player-leash-fail-target-tethered";
            return true;
        }

        if (HasComp<TetheredComponent>(puller))
        {
            locId = "player-leash-fail-puller-tethered";
            return true;
        }

        if (!TryComp<PhysicsComponent>(target, out var physics))
        {
            locId = "player-leash-fail-physics";
            return true;
        }

        if (physics.BodyType == BodyType.Static || _container.IsEntityInContainer(target))
        {
            locId = "player-leash-fail-container";
            return true;
        }

        if (TryComp<PlayerLeashPullerComponent>(puller, out var busy) &&
            ActiveLeashTargetCount(busy) >= busy.MaxLeashTargets &&
            !TryGetLeashSlot(busy, target, out _))
        {
            locId = "player-leash-verb-msg-too-many";
            return true;
        }

        var massLimit = TryComp<PlayerLeashPullerComponent>(puller, out var pullerLeash)
            ? pullerLeash.MassLimit
            : PlayerLeashPullerComponent.DefaultMassLimit;

        if (physics.Mass > massLimit)
        {
            locId = "player-leash-fail-mass";
            return true;
        }

        if (TryComp<StrapComponent>(target, out var strap) && strap.BuckledEntities.Count > 0)
        {
            locId = "player-leash-fail-buckled";
            return true;
        }

        if (!_interaction.InRangeUnobstructed(puller, target))
        {
            locId = "player-leash-fail-range";
            return true;
        }

        return false;
    }

    private void StartLeash(EntityUid puller, EntityUid target)
    {
        if (!TryGetHeldLeash(puller, out _, out var leashItem))
            return;

        var leash = EnsureComp<PlayerLeashPullerComponent>(puller);
        if (!TryGetFreeLeashSlot(leash, out var slot))
            return;

        leash.MaxForce = leashItem.MaxForce;
        leash.Frequency = leashItem.Frequency;
        leash.DampingRatio = leashItem.DampingRatio;
        leash.MassLimit = leashItem.MassLimit;
        leash.MaxLeashDistance = leashItem.MaxLeashDistance;
        leash.LineColor = leashItem.LineColor;
        leash.MaxLeashTargets = Math.Clamp(leashItem.MaxLeashTargets, 1, PlayerLeashPullerComponent.DefaultMaxLeashTargets);
        var startDistance = (_transform.GetWorldPosition(puller) - _transform.GetWorldPosition(target)).Length();
        SetCurrentDistance(leash, slot, Math.Clamp(startDistance, leash.MinLeashDistance, leash.MaxLeashDistance));

        PhysicsComponent? targetPhysics = null;
        TransformComponent? targetXform = null;
        if (!Resolve(target, ref targetPhysics, ref targetXform))
        {
            if (!HasAnyLeashTarget(leash))
                RemComp<PlayerLeashPullerComponent>(puller);
            return;
        }

        _transform.Unanchor(target, targetXform);

        SetFollower(leash, slot, target);
        var tethered = EnsureComp<TetheredComponent>(target);
        _physics.SetBodyStatus(target, targetPhysics, BodyStatus.InAir, false);
        _physics.SetSleepingAllowed(target, targetPhysics, false);
        tethered.Tetherer = puller;
        tethered.OriginalAngularDamping = targetPhysics.AngularDamping;
        _physics.SetAngularDamping(target, targetPhysics, targetPhysics.AngularDamping);
        _physics.SetLinearDamping(target, targetPhysics, 0f);
        _physics.SetAngularVelocity(target, 0f, body: targetPhysics);
        _physics.WakeBody(target, body: targetPhysics);

        var thrown = EnsureComp<ThrownItemComponent>(target);
        thrown.Thrower = puller;
        _blocker.UpdateCanMove(target);

        var tether = Spawn(TetherPrototype, _transform.GetMapCoordinates(target));
        var tetherPhysics = Comp<PhysicsComponent>(tether);
        SetAnchor(leash, slot, tether);
        _physics.WakeBody(tether);

        var joint = _joints.CreateMouseJoint(tether, target, id: PlayerLeashPullerComponent.LeashJointId);
        SharedJointSystem.LinearStiffness(leash.Frequency, leash.DampingRatio, tetherPhysics.Mass, targetPhysics.Mass,
            out var stiffness, out var damping);
        joint.Stiffness = stiffness;
        joint.Damping = damping;
        joint.MaxForce = leash.MaxForce;

        Dirty(target, tethered);
        Dirty(puller, leash);

        _transform.SetWorldPosition(tether, _transform.GetWorldPosition(puller));
    }

    /// <summary>
    /// On failure, <paramref name="leashItemUid"/> is invalid and <paramref name="leashComp"/> is null.
    /// </summary>
    private bool TryGetHeldLeash(EntityUid user, out EntityUid leashItemUid, [NotNullWhen(true)] out LeashComponent? leashComp)
    {
        leashItemUid = EntityUid.Invalid;
        leashComp = null;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (!TryComp(held, out LeashComponent? comp))
                continue;

            leashComp = comp;
            leashItemUid = held;
            return true;
        }

        return false;
    }

    public void StopLeash(EntityUid puller, PlayerLeashPullerComponent leash, bool land = true)
    {
        for (var slot = 0; slot < PlayerLeashPullerComponent.DefaultMaxLeashTargets; slot++)
        {
            StopLeashSlot(puller, leash, slot, land, removeComponent: false);
        }

        RemComp<PlayerLeashPullerComponent>(puller);
    }

    public bool HasLeashTarget(PlayerLeashPullerComponent leash, EntityUid target)
    {
        return TryGetLeashSlot(leash, target, out _);
    }

    public void StopLeashTarget(EntityUid puller, PlayerLeashPullerComponent leash, EntityUid target, bool land = true)
    {
        if (!TryGetLeashSlot(leash, target, out var slot))
            return;

        StopLeashSlot(puller, leash, slot, land);
    }

    private void StopLeashSlot(EntityUid puller, PlayerLeashPullerComponent leash, int slot, bool land = true, bool removeComponent = true)
    {
        var follower = GetFollower(leash, slot);
        var anchor = GetAnchor(leash, slot);

        if (follower == null && anchor == null)
            return;

        if (anchor is { } a)
        {
            _joints.RemoveJoint(a, PlayerLeashPullerComponent.LeashJointId);
            if (_net.IsServer)
                QueueDel(a);
        }

        if (follower is { } f)
        {
            if (TryComp<PhysicsComponent>(f, out var targetPhysics))
            {
                if (land)
                {
                    var thrown = EnsureComp<ThrownItemComponent>(f);
                    _thrown.LandComponent(f, thrown, targetPhysics, true);
                    _thrown.StopThrow(f, thrown);
                }

                _physics.SetBodyStatus(f, targetPhysics, BodyStatus.OnGround);
                _physics.SetSleepingAllowed(f, targetPhysics, true);
                if (TryComp<TetheredComponent>(f, out var tethered))
                    _physics.SetAngularDamping(f, targetPhysics, tethered.OriginalAngularDamping);
            }

            RemComp<TetheredComponent>(f);
            _blocker.UpdateCanMove(f);
        }

        SetFollower(leash, slot, null);
        SetAnchor(leash, slot, null);
        SetCurrentDistance(leash, slot, leash.MinLeashDistance);

        if (removeComponent && !HasAnyLeashTarget(leash))
        {
            RemComp<PlayerLeashPullerComponent>(puller);
            return;
        }

        Dirty(puller, leash);
    }

    private void AdjustLeashDistance(EntityUid puller, PlayerLeashPullerComponent leash, float delta, EntityUid? target)
    {
        for (var slot = 0; slot < PlayerLeashPullerComponent.DefaultMaxLeashTargets; slot++)
        {
            var follower = GetFollower(leash, slot);
            if (follower == null || target != null && follower != target)
                continue;

            var current = GetCurrentDistance(leash, slot);
            var next = Math.Clamp(current + delta, leash.MinLeashDistance, leash.MaxLeashDistance);
            if (Math.Abs(next - current) < 0.001f)
                continue;

            SetCurrentDistance(leash, slot, next);
            _popup.PopupPredicted(Loc.GetString("player-leash-popup-distance", ("distance", MathF.Round(next, 1))), puller, puller);
        }

        Dirty(puller, leash);
    }

    private void YankLeashTargets(EntityUid puller, PlayerLeashPullerComponent leash, EntityUid? target)
    {
        for (var slot = 0; slot < PlayerLeashPullerComponent.DefaultMaxLeashTargets; slot++)
        {
            var follower = GetFollower(leash, slot);
            if (follower is not { } followerUid || Deleted(followerUid) || target != null && followerUid != target)
                continue;

            YankLeashTarget(puller, leash, followerUid, GetCurrentDistance(leash, slot));
        }
    }

    private void YankLeashTarget(EntityUid puller, PlayerLeashPullerComponent leash, EntityUid follower, float leashDistance)
    {
        if (!_mob.IsAlive(puller) || !_mob.IsAlive(follower))
            return;

        if (!_interaction.InRangeUnobstructed(puller, follower, range: MathF.Max(YankInteractionRange, leashDistance), popup: true))
            return;

        var pullerPos = _transform.GetWorldPosition(puller);
        var followerPos = _transform.GetWorldPosition(follower);
        var diff = pullerPos - followerPos;
        var distance = diff.Length();

        if (distance > 0.001f && _net.IsServer)
        {
            var normalized = diff / distance;
            var targetPos = pullerPos - normalized * leash.MinLeashDistance;
            _transform.SetWorldPosition(follower, targetPos);
        }

        _stun.TryKnockdown(follower, PullKnockdownDuration, refresh: true, autoStand: true, drop: true, force: true);
        _popup.PopupPredicted(Loc.GetString("player-leash-popup-yank"), null, puller, puller);
        _popup.PopupPredicted(Loc.GetString("player-leash-popup-yanked"), null, follower, follower);
    }

    private void PopupLeashSnapped(EntityUid puller, EntityUid follower)
    {
        _popup.PopupPredicted(Loc.GetString("player-leash-popup-snapped"), null, puller, puller);
        _popup.PopupPredicted(Loc.GetString("player-leash-popup-snapped"), null, follower, follower);
    }

    private void PopupLeashSnapped(EntityUid puller, PlayerLeashPullerComponent leash)
    {
        _popup.PopupPredicted(Loc.GetString("player-leash-popup-snapped"), null, puller, puller);

        for (var slot = 0; slot < PlayerLeashPullerComponent.DefaultMaxLeashTargets; slot++)
        {
            if (GetFollower(leash, slot) is { } follower && !Deleted(follower))
                _popup.PopupPredicted(Loc.GetString("player-leash-popup-snapped"), null, follower, follower);
        }
    }

    private static bool HasAnyLeashTarget(PlayerLeashPullerComponent leash)
    {
        return leash.Following != null || leash.Following2 != null || leash.Following3 != null;
    }

    private static int ActiveLeashTargetCount(PlayerLeashPullerComponent leash)
    {
        var count = 0;

        if (leash.Following != null)
            count++;
        if (leash.Following2 != null)
            count++;
        if (leash.Following3 != null)
            count++;

        return count;
    }

    private static bool TryGetLeashSlot(PlayerLeashPullerComponent leash, EntityUid target, out int slot)
    {
        for (slot = 0; slot < PlayerLeashPullerComponent.DefaultMaxLeashTargets; slot++)
        {
            if (GetFollower(leash, slot) == target)
                return true;
        }

        slot = -1;
        return false;
    }

    private static bool TryGetFreeLeashSlot(PlayerLeashPullerComponent leash, out int slot)
    {
        for (slot = 0; slot < Math.Clamp(leash.MaxLeashTargets, 1, PlayerLeashPullerComponent.DefaultMaxLeashTargets); slot++)
        {
            if (GetFollower(leash, slot) == null)
                return true;
        }

        slot = -1;
        return false;
    }

    private static EntityUid? GetFollower(PlayerLeashPullerComponent leash, int slot)
    {
        return slot switch
        {
            0 => leash.Following,
            1 => leash.Following2,
            2 => leash.Following3,
            _ => null,
        };
    }

    private static void SetFollower(PlayerLeashPullerComponent leash, int slot, EntityUid? value)
    {
        switch (slot)
        {
            case 0:
                leash.Following = value;
                break;
            case 1:
                leash.Following2 = value;
                break;
            case 2:
                leash.Following3 = value;
                break;
        }
    }

    private static EntityUid? GetAnchor(PlayerLeashPullerComponent leash, int slot)
    {
        return slot switch
        {
            0 => leash.TetherAnchor,
            1 => leash.TetherAnchor2,
            2 => leash.TetherAnchor3,
            _ => null,
        };
    }

    private static void SetAnchor(PlayerLeashPullerComponent leash, int slot, EntityUid? value)
    {
        switch (slot)
        {
            case 0:
                leash.TetherAnchor = value;
                break;
            case 1:
                leash.TetherAnchor2 = value;
                break;
            case 2:
                leash.TetherAnchor3 = value;
                break;
        }
    }

    private static float GetCurrentDistance(PlayerLeashPullerComponent leash, int slot)
    {
        return slot switch
        {
            0 => leash.CurrentLeashDistance,
            1 => leash.CurrentLeashDistance2,
            2 => leash.CurrentLeashDistance3,
            _ => leash.CurrentLeashDistance,
        };
    }

    private static void SetCurrentDistance(PlayerLeashPullerComponent leash, int slot, float value)
    {
        switch (slot)
        {
            case 0:
                leash.CurrentLeashDistance = value;
                break;
            case 1:
                leash.CurrentLeashDistance2 = value;
                break;
            case 2:
                leash.CurrentLeashDistance3 = value;
                break;
        }
    }
}

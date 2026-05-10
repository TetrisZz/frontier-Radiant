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
    }

    private void OnPullerTerminating(EntityUid uid, PlayerLeashPullerComponent comp, ref EntityTerminatingEvent args)
    {
        StopLeash(uid, comp);
    }

    private void OnTetheredTerminating(EntityUid uid, TetheredComponent tethered, ref EntityTerminatingEvent args)
    {
        if (!TryComp<PlayerLeashPullerComponent>(tethered.Tetherer, out var pullerComp))
            return;

        if (pullerComp.Following != uid)
            return;

        StopLeash(tethered.Tetherer, pullerComp, land: false);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var q = EntityQueryEnumerator<PlayerLeashPullerComponent, TransformComponent>();

        while (q.MoveNext(out var puller, out var leash, out var pullerXform))
        {
            if (leash.TetherAnchor is not { } anchor || leash.Following is not { } follower)
                continue;

            if (Deleted(puller) || Deleted(follower) || Deleted(anchor))
            {
                if (!Deleted(puller) && !Deleted(follower))
                    PopupLeashSnapped(puller, follower);
                StopLeash(puller, leash, land: !Deleted(puller));
                continue;
            }

            if (!TryGetHeldLeash(puller, out _, out _))
            {
                PopupLeashSnapped(puller, follower);
                StopLeash(puller, leash);
                continue;
            }

            var mapA = Transform(puller).MapID;
            var mapB = Transform(follower).MapID;
            if (mapA != mapB || mapA == MapId.Nullspace)
            {
                PopupLeashSnapped(puller, follower);
                StopLeash(puller, leash);
                continue;
            }

            var pPos = _transform.GetWorldPosition(pullerXform);
            var fPos = _transform.GetWorldPosition(follower);

            var hardLimit = leash.MaxLeashDistance;
            var distanceSquared = (pPos - fPos).LengthSquared();
            if (distanceSquared > hardLimit * hardLimit)
            {
                PopupLeashSnapped(puller, follower);
                StopLeash(puller, leash);
                continue;
            }

            var currentDistance = Math.Clamp(leash.CurrentLeashDistance, leash.MinLeashDistance, leash.MaxLeashDistance);
            leash.CurrentLeashDistance = currentDistance;
            var distance = MathF.Sqrt(distanceSquared);

            if (distance <= currentDistance || distance <= 0.001f)
                _transform.SetWorldPosition(anchor, fPos);
            else
                _transform.SetWorldPosition(anchor, pPos + (fPos - pPos) / distance * currentDistance);
        }
    }

    private void AddSelfDetachVerb(EntityUid uid, PlayerLeashPullerComponent comp, ref GetVerbsEvent<InteractionVerb> args)
    {
        if (args.Target != uid || args.User != uid || comp.Following is not { } follower || Deleted(follower))
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
            Act = () => AdjustLeashDistance(uid, comp, -comp.DistanceAdjustStep),
            Priority = 19,
            Impact = LogImpact.Low,
        };
        args.Verbs.Add(tighten);

        InteractionVerb loosen = new()
        {
            Text = Loc.GetString("player-leash-verb-loosen"),
            Act = () => AdjustLeashDistance(uid, comp, comp.DistanceAdjustStep),
            Priority = 18,
            Impact = LogImpact.Low,
        };
        args.Verbs.Add(loosen);

        InteractionVerb yank = new()
        {
            Text = Loc.GetString("player-leash-verb-yank"),
            Act = () => YankLeashTarget(uid, comp),
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

        if (existing is { Following: { } current } && current == args.Target)
        {
            var puller = args.User;
            var leash = existing;
            InteractionVerb detach = new()
            {
                Text = Loc.GetString("player-leash-verb-detach-target"),
                Act = () => StopLeash(puller, leash),
                Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/unbuckle.svg.192dpi.png")),
                Priority = 20,
                Impact = LogImpact.Low,
            };
            args.Verbs.Add(detach);
            return;
        }

        if (!TryGetHeldLeash(args.User, out var leashUid, out var leashComp))
            return;
        _ = leashUid;
        _ = leashComp;

        InteractionVerb attach = new()
        {
            Priority = 10,
            Impact = LogImpact.Low,
            Icon = new SpriteSpecifier.Texture(new("/Textures/Interface/VerbIcons/group.svg.192dpi.png")),
            Text = Loc.GetString("player-leash-verb-attach"),
        };

        if (existing is { Following: not null } && existing.Following != args.Target)
        {
            attach.Disabled = true;
            attach.Message = Loc.GetString("player-leash-verb-msg-already-pulling");
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
            _ = leashUid;
            _ = leashComp;

            if (TryGetAttachBlocker(pullerUid, targetUid, out var fail))
            {
                _popup.PopupPredicted(Loc.GetString(fail), pullerUid, pullerUid);
                return;
            }

            StartLeash(pullerUid, targetUid);
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
            busy.Following != null &&
            busy.Following != target)
        {
            locId = "player-leash-verb-msg-already-pulling";
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
        if (TryComp<PlayerLeashPullerComponent>(puller, out var existing) && existing.Following != null)
            StopLeash(puller, existing);

        if (!TryGetHeldLeash(puller, out _, out var leashItem))
            return;

        var leash = EnsureComp<PlayerLeashPullerComponent>(puller);
        leash.MaxForce = leashItem.MaxForce;
        leash.Frequency = leashItem.Frequency;
        leash.DampingRatio = leashItem.DampingRatio;
        leash.MassLimit = leashItem.MassLimit;
        leash.MaxLeashDistance = leashItem.MaxLeashDistance;
        leash.LineColor = leashItem.LineColor;
        var startDistance = (_transform.GetWorldPosition(puller) - _transform.GetWorldPosition(target)).Length();
        leash.CurrentLeashDistance = Math.Clamp(startDistance, leash.MinLeashDistance, leash.MaxLeashDistance);

        PhysicsComponent? targetPhysics = null;
        TransformComponent? targetXform = null;
        if (!Resolve(target, ref targetPhysics, ref targetXform))
        {
            RemComp<PlayerLeashPullerComponent>(puller);
            return;
        }

        _transform.Unanchor(target, targetXform);

        leash.Following = target;
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
        leash.TetherAnchor = tether;
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

    private bool TryGetHeldLeash(EntityUid user, [NotNullWhen(true)] out EntityUid? leashUid, [NotNullWhen(true)] out LeashComponent? leash)
    {
        leashUid = null;
        leash = null;

        foreach (var held in _hands.EnumerateHeld(user))
        {
            if (!TryComp(held, out leash))
                continue;

            leashUid = held;
            return true;
        }

        return false;
    }

    public void StopLeash(EntityUid puller, PlayerLeashPullerComponent leash, bool land = true)
    {
        var follower = leash.Following;
        var anchor = leash.TetherAnchor;

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

        RemComp<PlayerLeashPullerComponent>(puller);
    }

    private void AdjustLeashDistance(EntityUid puller, PlayerLeashPullerComponent leash, float delta)
    {
        var next = Math.Clamp(leash.CurrentLeashDistance + delta, leash.MinLeashDistance, leash.MaxLeashDistance);
        if (Math.Abs(next - leash.CurrentLeashDistance) < 0.001f)
            return;

        leash.CurrentLeashDistance = next;
        Dirty(puller, leash);
        _popup.PopupPredicted(Loc.GetString("player-leash-popup-distance", ("distance", MathF.Round(next, 1))), puller, puller);
    }

    private void YankLeashTarget(EntityUid puller, PlayerLeashPullerComponent leash)
    {
        if (leash.Following is not { } follower || Deleted(follower))
            return;

        if (!_mob.IsAlive(puller) || !_mob.IsAlive(follower))
            return;

        if (!_interaction.InRangeUnobstructed(puller, follower, range: MathF.Max(YankInteractionRange, leash.CurrentLeashDistance), popup: true))
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
}

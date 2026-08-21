using Content.Server.Emoting;
using Content.Server.Popups;
using Content.Shared._Goobstation.Vehicles;
using Content.Shared._radiant.Vehicles;
using Content.Shared.Buckle;
using Content.Shared.Buckle.Components;
using Content.Shared.DoAfter;
using Content.Shared.Emoting;
using Content.Shared.Inventory.VirtualItem;
using Content.Shared.Verbs;
using Robust.Shared.Network;
using Robust.Shared.Map;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Vehicles;

public sealed class VehicleTrickSystem : EntitySystem
{
    [Dependency] private readonly AnimatedEmotesSystem _animatedEmotes = default!;
    [Dependency] private readonly SharedBuckleSystem _buckle = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedVirtualItemSystem _virtualItems = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<VehicleTrickComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<VehicleTrickComponent, VehicleTrickDoAfterEvent>(OnTrickFinished);
        SubscribeLocalEvent<VehicleTrickComponent, UnstrappedEvent>(OnUnstrapped);
        SubscribeLocalEvent<VehicleTrickComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnGetVerbs(Entity<VehicleTrickComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract ||
            !TryComp<VehicleComponent>(entity, out var vehicle) ||
            vehicle.Driver != args.User ||
            entity.Comp.User != null ||
            _timing.CurTime < entity.Comp.NextTrick)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("vehicle-trick-verb"),
            Act = () => StartTrick(entity, user, vehicle),
        });
    }

    private void StartTrick(Entity<VehicleTrickComponent> entity, EntityUid user, VehicleComponent vehicle)
    {
        if (vehicle.Driver != user || entity.Comp.User != null || _timing.CurTime < entity.Comp.NextTrick)
            return;

        var gripsNeeded = Math.Max(0, 2 - vehicle.RequiredHands);
        for (var i = 0; i < gripsNeeded; i++)
        {
            // A virtual copy of the vehicle itself fires VirtualItemDeletedEvent on cleanup,
            // which the vehicle system treats as a request to dismount. Use a visual-only
            // helper entity so successful tricks never unstrap the rider.
            var gripSource = Spawn(entity.Comp.GripPrototype, MapCoordinates.Nullspace);
            if (!_virtualItems.TrySpawnVirtualItemInHand(gripSource, user, out var grip) || grip == null)
            {
                QueueDel(gripSource);
                Cleanup(entity);
                _popup.PopupEntity(Loc.GetString("vehicle-trick-hands-busy"), entity.Owner, user);
                return;
            }

            entity.Comp.TemporaryGrips.Add(gripSource);
            entity.Comp.TemporaryGrips.Add(grip.Value);
        }

        entity.Comp.User = user;
        entity.Comp.IsFailure = _random.Prob(entity.Comp.FailureChance);
        entity.Comp.IsFlip = !entity.Comp.IsFailure && _random.Prob(entity.Comp.FlipChance);
        entity.Comp.NextTrick = _timing.CurTime + TimeSpan.FromSeconds(entity.Comp.Cooldown);

        var emote = entity.Comp.IsFlip ? "VehicleTrickFlip" : "Jump";
        PlayAnimation(user, emote);
        PlayAnimation(entity.Owner, emote);

        var selfMessage = entity.Comp.IsFlip ? "vehicle-trick-flip-self" : "vehicle-trick-self";
        var othersMessage = entity.Comp.IsFlip ? "vehicle-trick-flip-others" : "vehicle-trick-others";
        _popup.PopupEntity(Loc.GetString(selfMessage), user, user);
        _popup.PopupEntity(
            Loc.GetString(othersMessage, ("user", user)),
            user,
            Filter.PvsExcept(user, entityManager: EntityManager),
            true);

        var doAfter = new DoAfterArgs(
            EntityManager,
            user,
            entity.Comp.Duration,
            new VehicleTrickDoAfterEvent(),
            entity.Owner,
            target: entity.Owner)
        {
            BreakOnDamage = true,
            BreakOnMove = false,
            NeedHand = false,
            RequireCanInteract = false,
            BlockDuplicate = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            Cleanup(entity);
    }

    private void PlayAnimation(EntityUid target, string emote)
    {
        var animated = EnsureComp<AnimatedEmotesComponent>(target);
        _animatedEmotes.PlayEmoteAnimation(target, animated, emote);
    }

    private void OnTrickFinished(Entity<VehicleTrickComponent> entity, ref VehicleTrickDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        var user = entity.Comp.User;
        var failed = entity.Comp.IsFailure;
        var detach = failed && _random.Prob(entity.Comp.DetachOnFailureChance);
        Cleanup(entity);

        if (user == null || !failed)
            return;

        var userUid = user.Value;

        var selfMessage = detach ? "vehicle-trick-fall-self" : "vehicle-trick-failure-self";
        var othersMessage = detach ? "vehicle-trick-fall-others" : "vehicle-trick-failure-others";
        _popup.PopupEntity(Loc.GetString(selfMessage), userUid, userUid);
        _popup.PopupEntity(
            Loc.GetString(othersMessage, ("user", userUid)),
            userUid,
            Filter.PvsExcept(userUid, entityManager: EntityManager),
            true);

        if (detach)
            _buckle.TryUnbuckle(userUid, userUid, popup: false);
    }

    private void OnUnstrapped(Entity<VehicleTrickComponent> entity, ref UnstrappedEvent args)
    {
        if (entity.Comp.User == args.Buckle.Owner)
            Cleanup(entity);
    }

    private void OnShutdown(Entity<VehicleTrickComponent> entity, ref ComponentShutdown args)
    {
        Cleanup(entity);
    }

    private void Cleanup(Entity<VehicleTrickComponent> entity)
    {
        var user = entity.Comp.User;

        foreach (var grip in entity.Comp.TemporaryGrips)
        {
            if (Exists(grip))
                QueueDel(grip);
        }

        ClearAnimation(entity.Owner);
        if (user is { } userUid)
            ClearAnimation(userUid);

        entity.Comp.TemporaryGrips.Clear();
        entity.Comp.User = null;
        entity.Comp.IsFlip = false;
        entity.Comp.IsFailure = false;
    }

    private void ClearAnimation(EntityUid target)
    {
        if (!TryComp<AnimatedEmotesComponent>(target, out var animated))
            return;

        animated.Emote = null;
        Dirty(target, animated);
    }
}

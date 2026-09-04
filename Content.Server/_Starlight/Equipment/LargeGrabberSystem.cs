using Content.Server.Interaction;
using Content.Server.PowerCell;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Mech.Equipment.Components;
using Content.Shared.Whitelist;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Utility;

namespace Content.Server._Starlight.Equipment;

// Radiant sector: portable variant of the mech grabber logic.
public sealed class LargeGrabberSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly InteractionSystem _interaction = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelist = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<LargeGrabberComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<LargeGrabberComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<LargeGrabberComponent, GrabberDoAfterEvent>(OnGrab);
        SubscribeLocalEvent<LargeGrabberComponent, EntGotRemovedFromContainerMessage>(OnRemovedFromContainer);
    }

    private void OnStartup(EntityUid uid, LargeGrabberComponent comp, ComponentStartup args)
        => comp.ItemContainer = _container.EnsureContainer<Container>(uid, "item-container");

    private void OnInteract(EntityUid uid, LargeGrabberComponent comp, AfterInteractEvent args)
    {
        if (args.Handled)
            return;

        if (_toggle.IsActivated(uid))
        {
            if (comp.ItemContainer.ContainedEntities.TryFirstOrNull(out var item) && item.HasValue)
                RemoveItem(uid, args.User, item.Value, comp);
            UpdateState(uid, comp);
            args.Handled = true;
            return;
        }

        var target = args.Target;
        if (target == null || target == args.User || comp.DoAfter != null || comp.ItemContainer.Count >= comp.MaxContents)
            return;
        if (TryComp<PhysicsComponent>(target, out var physics) && physics.BodyType == BodyType.Static)
            return;
        if (Transform(target.Value).Anchored || !_interaction.InRangeUnobstructed(args.User, target.Value))
            return;
        if (_whitelist.IsWhitelistPass(comp.Blacklist, target.Value) || !_powerCell.TryUseCharge(uid, comp.GrabEnergyCost))
            return;

        args.Handled = true;
        comp.AudioStream = _audio.PlayPvs(comp.GrabSound, uid)?.Entity;
        var doAfter = new DoAfterArgs(EntityManager, args.User, comp.GrabDelay, new GrabberDoAfterEvent(), uid, target: target, used: uid)
        {
            BreakOnMove = true,
        };
        _doAfter.TryStartDoAfter(doAfter, out comp.DoAfter);
    }

    private void OnGrab(EntityUid uid, LargeGrabberComponent comp, DoAfterEvent args)
    {
        comp.DoAfter = null;
        if (args.Cancelled)
        {
            comp.AudioStream = _audio.Stop(comp.AudioStream);
            return;
        }
        if (args.Handled || args.Args.Target is not { } target)
            return;

        _container.Insert(target, comp.ItemContainer);
        UpdateState(uid, comp);
        args.Handled = true;
    }

    private void OnRemovedFromContainer(EntityUid uid, LargeGrabberComponent comp, EntGotRemovedFromContainerMessage args)
    {
        if (!comp.DropOnContainerChange)
            return;
        while (comp.ItemContainer.ContainedEntities.TryFirstOrNull(out var item) && item.HasValue)
            RemoveItem(uid, args.Container.Owner, item.Value, comp);
        UpdateState(uid, comp);
    }

    private void RemoveItem(EntityUid uid, EntityUid user, EntityUid item, LargeGrabberComponent comp)
    {
        _container.Remove(item, comp.ItemContainer);
        var userXform = Transform(user);
        _transform.AttachToGridOrMap(item, Transform(item));
        var (position, rotation) = _transform.GetWorldPositionRotation(userXform);
        _transform.SetWorldPositionRotation(item, position + rotation.RotateVec(comp.DepositOffset), Angle.Zero);
    }

    private void UpdateState(EntityUid uid, LargeGrabberComponent comp)
    {
        if (comp.ItemContainer.Count == 0)
            _toggle.TryDeactivate(uid);
        else if (comp.ItemContainer.Count >= comp.MaxContents)
            _toggle.TryActivate(uid);
    }
}

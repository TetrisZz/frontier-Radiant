using System.Numerics;
using Content.Server.Tiles;
using Content.Shared.Actions;
using Content.Shared.Actions.Components;
using Content.Shared._radiant.Abilities.Shadowkin;
using Content.Shared.Examine;
using Content.Shared.Maps;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Map;
using Robust.Shared.Map.Components;
using Robust.Shared.Random;

namespace Content.Server._radiant.Abilities.Shadowkin;

/// <summary>
/// Server-authoritative implementation of the shadowkin's random short-range teleport.
/// </summary>
public sealed class ShadowkinShadeStepSystem : EntitySystem
{
    [Dependency] private readonly SharedActionsSystem _actions = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly TurfSystem _turf = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ShadowkinShadeStepComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<ShadowkinShadeStepComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<ShadowkinShadeStepComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<ShadowkinShadeStepComponent, ShadowkinShadeStepEvent>(OnShadeStep);
        SubscribeLocalEvent<ShadowkinShadeStepComponent, RejuvenateEvent>(OnRejuvenate);
    }

    private void OnRejuvenate(Entity<ShadowkinShadeStepComponent> entity, ref RejuvenateEvent args)
    {
        entity.Comp.Energy = entity.Comp.MaxEnergy;
        Dirty(entity);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ShadowkinShadeStepComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (component.Energy >= component.MaxEnergy)
                continue;

            component.Energy = MathF.Min(component.MaxEnergy, component.Energy + component.EnergyRegenPerSecond * frameTime);
            Dirty(uid, component);
        }
    }

    private void OnMapInit(Entity<ShadowkinShadeStepComponent> entity, ref MapInitEvent args)
    {
        if (TryComp(entity, out ActionsComponent? actions))
            _actions.AddAction(entity, ref entity.Comp.ActionEntity, entity.Comp.Action, component: actions);
    }

    private void OnShutdown(Entity<ShadowkinShadeStepComponent> entity, ref ComponentShutdown args)
    {
        _actions.RemoveAction(entity.Owner, entity.Comp.ActionEntity);
    }

    private void OnShadeStep(Entity<ShadowkinShadeStepComponent> entity, ref ShadowkinShadeStepEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (entity.Comp.Energy < entity.Comp.EnergyCost)
        {
            _popup.PopupEntity(Loc.GetString("shadowkin-shade-step-exhausted"), entity, args.Performer);
            return;
        }

        if (!TryFindDestination(entity.Owner, entity.Comp.Range, out var destination))
        {
            _popup.PopupEntity(Loc.GetString("shadowkin-shade-step-no-destination"), entity, args.Performer);
            return;
        }

        entity.Comp.Energy -= entity.Comp.EnergyCost;
        Dirty(entity);

        var original = Transform(entity.Owner).Coordinates;
        _transform.SetCoordinates(entity.Owner, destination);
        _transform.AttachToGridOrMap(entity.Owner);

        var teleportSound = new SoundPathSpecifier("/Audio/_Goobstation/Effects/Shadowkin/shadeskip.ogg");
        _audio.PlayPvs(teleportSound, original);
        _audio.PlayPvs(teleportSound, destination);
        _popup.PopupEntity(Loc.GetString("shadowkin-shade-step-success",
            ("energy", MathF.Floor(entity.Comp.Energy)),
            ("maxEnergy", entity.Comp.MaxEnergy)), entity, args.Performer);
    }

    private void OnExamined(Entity<ShadowkinShadeStepComponent> entity, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("shadowkin-shade-step-examine",
            ("energy", MathF.Floor(entity.Comp.Energy)),
            ("maxEnergy", entity.Comp.MaxEnergy)));
    }

    private bool TryFindDestination(EntityUid user, float range, out EntityCoordinates destination)
    {
        var origin = Transform(user).Coordinates;
        var rangeInt = (int) MathF.Ceiling(range);

        for (var attempt = 0; attempt < 24; attempt++)
        {
            var offset = new Vector2(_random.Next(-rangeInt, rangeInt + 1), _random.Next(-rangeInt, rangeInt + 1));
            if (offset == Vector2.Zero || offset.LengthSquared() > range * range)
                continue;

            var candidate = origin.Offset(offset);
            if (!IsSafeDestination(candidate))
                continue;

            destination = candidate;
            return true;
        }

        destination = default;
        return false;
    }

    private bool IsSafeDestination(EntityCoordinates coordinates)
    {
        if (!_turf.TryGetTileRef(coordinates, out TileRef? tileRef) || tileRef is not { } tile || tile.Tile.IsEmpty)
            return false;

        if (_turf.IsTileBlocked(tile, CollisionGroup.MobMask))
            return false;

        if (!TryComp(tile.GridUid, out MapGridComponent? grid))
            return false;

        var anchored = _map.GetAnchoredEntitiesEnumerator(tile.GridUid, grid, tile.GridIndices);
        while (anchored.MoveNext(out var entity))
        {
            if (HasComp<TileEntityEffectComponent>(entity))
                return false;
        }

        return true;
    }
}

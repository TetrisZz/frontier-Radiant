using Content.Server.Fluids.EntitySystems;
using Content.Shared._radiant.Abilities.Feroxi;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Maps;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Abilities.Feroxi;

/// <summary>
/// Applies the feroxi water bonus only while standing in a puddle containing water.
/// </summary>
public sealed class FeroxiWaterAffinitySystem : EntitySystem
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";

    [Dependency] private readonly MovementSpeedModifierSystem _movement = default!;
    [Dependency] private readonly SharedMapSystem _map = default!;
    [Dependency] private readonly PuddleSystem _puddles = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly TurfSystem _turf = default!;

    private float _updateAccumulator;

    public override void Initialize()
    {
        SubscribeLocalEvent<FeroxiWaterAffinityComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    public override void Update(float frameTime)
    {
        _updateAccumulator += frameTime;
        if (_updateAccumulator < 0.2f)
            return;

        _updateAccumulator = 0f;
        var query = EntityQueryEnumerator<FeroxiWaterAffinityComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            var inWater = IsStandingInWater(uid);
            if (component.InWater == inWater)
                continue;

            component.InWater = inWater;
            _movement.RefreshMovementSpeedModifiers(uid);
        }
    }

    private void OnRefreshSpeed(Entity<FeroxiWaterAffinityComponent> entity, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (entity.Comp.InWater)
            args.ModifySpeed(entity.Comp.WaterSpeedModifier);
    }

    private bool IsStandingInWater(EntityUid entity)
    {
        if (!_turf.TryGetTileRef(Transform(entity).Coordinates, out var tileRef) ||
            tileRef is not { } tile)
        {
            return false;
        }

        if (_puddles.TryGetPuddle(tile, out var puddle) && IsWaterPuddle(puddle))
            return true;

        if (!TryComp(tile.GridUid, out MapGridComponent? grid))
            return false;

        var anchored = _map.GetAnchoredEntitiesEnumerator(tile.GridUid, grid, tile.GridIndices);
        while (anchored.MoveNext(out var anchoredEntity))
        {
            if (HasComp<FeroxiWaterSourceComponent>(anchoredEntity))
                return true;
        }

        return false;
    }

    private bool IsWaterPuddle(EntityUid puddleUid)
    {
        if (!TryComp(puddleUid, out PuddleComponent? puddle) ||
            !_solutions.ResolveSolution(puddleUid, puddle.SolutionName, ref puddle.Solution, out var solution))
        {
            return false;
        }

        return solution.GetTotalPrototypeQuantity(WaterReagent) > FixedPoint2.Zero;
    }
}

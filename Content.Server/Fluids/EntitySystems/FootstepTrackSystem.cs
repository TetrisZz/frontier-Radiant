using System.Numerics;
using System.Collections.Generic;
using Content.Server.Decals;
using Content.Server.Fluids.Components;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Fluids.Components;
using Content.Shared.Maps;
using Content.Shared.Standing;
using Robust.Shared.Map;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Fluids.EntitySystems;

/// <summary>
/// Cleanable, directional traces from contaminated shoes and bodies, plus occasional impact splashes.
/// Blood puddles are still handled by BloodstreamSystem.
/// </summary>
public sealed partial class FootstepTrackSystem : EntitySystem
{
    private const float VolumePerTrackStep = 15f;
    private const int MaxTrackSteps = 8;
    private static readonly TimeSpan WaterTrackLifetime = TimeSpan.FromSeconds(25);
    private const int MaxFootstepsPerTile = 6;
    private const int MaxCrawlTrailsPerTile = 4;
    private const int MaxSplashesPerTile = 3;

    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private PuddleSystem _puddles = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private IGameTiming _timing = default!;

    private readonly Queue<(EntityUid Grid, uint DecalId, TimeSpan Expires)> _dryingTracks = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<FootstepTrackComponent, MoveEvent>(OnMove);
        SubscribeLocalEvent<FootstepTrackComponent, DamageChangedEvent>(OnDamaged);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        while (_dryingTracks.TryPeek(out var track) && track.Expires <= _timing.CurTime)
        {
            _dryingTracks.Dequeue();
            if (Exists(track.Grid))
                _decals.RemoveDecal(track.Grid, track.DecalId);
        }
    }

    private void OnMove(Entity<FootstepTrackComponent> ent, ref MoveEvent args)
    {
        var track = ent.Comp;
        var tile = _turf.GetTileRef(args.NewPosition);
        if (tile == null || tile.Value.Tile.IsEmpty)
        {
            track.LastGrid = null;
            track.LastTile = null;
            track.RemainingSteps = 0;
            track.BloodOnFeet = false;
            track.WaterOnFeet = false;
            return;
        }

        var current = tile.Value;
        if (track.LastGrid == current.GridUid && track.LastTile == current.GridIndices)
            return;

        track.LastGrid = current.GridUid;
        track.LastTile = current.GridIndices;

        if (!TryComp<PhysicsComponent>(ent, out var physics) || physics.BodyStatus != BodyStatus.OnGround)
        {
            track.RemainingSteps = 0;
            track.BloodOnFeet = false;
            track.WaterOnFeet = false;
            return;
        }

        var previous = _turf.GetTileRef(args.OldPosition);
        if (previous == null || previous.Value.GridUid != current.GridUid)
            return;

        var direction = new Vector2(current.GridIndices.X - previous.Value.GridIndices.X,
            current.GridIndices.Y - previous.Value.GridIndices.Y);
        if (direction.LengthSquared() < 0.5f || direction.LengthSquared() > 2.1f)
            return; // Teleportation and grid transfers should not draw a line across the map.

        direction = Vector2.Normalize(direction);
        var rotation = new Angle(Math.Atan2(direction.Y, direction.X) - Math.PI / 2);
        var crawling = TryComp<StandingStateComponent>(ent, out var standing) && !standing.Standing;

        if (_puddles.TryGetPuddle(current, out var puddleUid) &&
            TryComp<PuddleComponent>(puddleUid, out var puddle) &&
            _solutions.ResolveSolution(puddleUid, puddle.SolutionName, ref puddle.Solution, out var solution) &&
            solution.Volume > 0)
        {
            track.TrackColor = solution.GetColor(_prototypes).WithAlpha(0.75f);
            var bloodVolume = 0f;
            var waterVolume = 0f;
            foreach (var reagent in solution.Contents)
            {
                if (IsBloodReagent(reagent.Reagent.Prototype))
                    bloodVolume += reagent.Quantity.Float();
                else if (reagent.Reagent.Prototype == "Water")
                    waterVolume += reagent.Quantity.Float();
            }

            track.BloodOnFeet = bloodVolume >= solution.Volume.Float() * 0.5f;
            track.WaterOnFeet = !track.BloodOnFeet && waterVolume >= solution.Volume.Float() * 0.5f;
            track.RemainingSteps = Math.Clamp((int) Math.Ceiling(solution.Volume.Float() / VolumePerTrackStep),
                1, MaxTrackSteps);
        }
        else if (track.RemainingSteps > 0)
        {
            track.RemainingSteps--;
            track.AlternateStep = !track.AlternateStep;

            var id = crawling
                ? track.AlternateStep ? "RadiantCrawlTrail1" : "RadiantCrawlTrail2"
                : !track.BloodOnFeet ? "footprint"
                : track.AlternateStep ? "RadiantFootstep1" : "RadiantFootstep2";

            AddTrace(current.GridUid, current.GridIndices, id,
                track.TrackColor, rotation, direction, crawling ? MaxCrawlTrailsPerTile : MaxFootstepsPerTile,
                track.WaterOnFeet);
        }

        if (!crawling || !TryComp<BloodstreamComponent>(ent, out var blood) || blood.BleedAmount <= 0)
            return;

        // A bleeding body leaves a separate drag mark; this never replaces the normal blood puddle.
        track.AlternateStep = !track.AlternateStep;
        var ordinaryBlood = blood.BloodReagent.Id == "Blood";
        var bloodId = ordinaryBlood
            ? track.AlternateStep ? "RadiantBloodCrawlTrail1" : "RadiantBloodCrawlTrail2"
            : track.AlternateStep ? "RadiantCrawlTrail1" : "RadiantCrawlTrail2";
        Color? bloodColor = ordinaryBlood ? null : _prototypes.Index(blood.BloodReagent).SubstanceColor.WithAlpha(0.85f);
        AddTrace(current.GridUid, current.GridIndices, bloodId,
            bloodColor, rotation, direction, MaxCrawlTrailsPerTile);
    }

    private static bool IsBloodReagent(string reagent)
    {
        return reagent is "Blood" or "InsectBlood" or "CopperBlood" or "AmmoniaBlood" or
            "ZombieBlood" or "ResomiBlood";
    }

    private void OnDamaged(Entity<FootstepTrackComponent> ent, ref DamageChangedEvent args)
    {
        if (!args.DamageIncreased || args.DamageDelta == null ||
            !TryComp<BloodstreamComponent>(ent, out var blood) || _timing.CurTime < ent.Comp.NextSplatter)
            return;

        var impact = 0f;
        foreach (var kind in new[] { "Blunt", "Slash", "Piercing" })
        {
            if (args.DamageDelta.DamageDict.TryGetValue(kind, out var damage) && damage > 0)
                impact += damage.Float();
        }

        if (impact < 4f || !_random.Prob(Math.Clamp(impact / 25f, 0.25f, 0.8f)))
            return;

        var tile = _turf.GetTileRef(Transform(ent).Coordinates);
        if (tile == null || tile.Value.Tile.IsEmpty)
            return;

        ent.Comp.NextSplatter = _timing.CurTime + TimeSpan.FromSeconds(2);
        var id = _random.Prob(0.5f) ? "RadiantBloodSplatter1" : "RadiantBloodSplatter2";
        var color = _prototypes.Index(blood.BloodReagent).SubstanceColor.WithAlpha(0.85f);
        AddTrace(tile.Value.GridUid, tile.Value.GridIndices, id,
            color, Angle.FromDegrees(_random.Next(0, 360)), Vector2.UnitY, MaxSplashesPerTile);
    }

    private void AddTrace(EntityUid grid, Vector2i tile, string id,
        Color? color, Angle rotation, Vector2 direction, int limit, bool dries = false)
    {
        var category = id == "footprint" ? "footprint"
            : id.StartsWith("RadiantFootstep") ? "RadiantFootstep"
            : id.StartsWith("RadiantBloodSplatter") ? "RadiantBloodSplatter" : "Trail";
        var existing = _decals.GetDecalsInRange(grid, tile, 1f,
            validDelegate: decal => category == "Trail"
                ? decal.Id.Contains("CrawlTrail")
                : category == "footprint" ? decal.Id == "footprint"
                : decal.Id.StartsWith(category));
        if (existing.Count >= limit)
            return;

        // Every new trace gets its own place on the tile, even if two walkers take the same path.
        var lane = existing.Count switch
        {
            1 => -0.12f,
            2 => 0.12f,
            3 => -0.22f,
            4 => 0.22f,
            _ => 0f,
        };
        var side = new Vector2(-direction.Y, direction.X) * lane;
        var forward = direction * (existing.Count >= 5 ? 0.12f : 0f);
        var position = new Vector2(tile.X, tile.Y) + side + forward;
        if (_decals.TryAddDecal(id, new EntityCoordinates(grid, position), out var decalId,
                color, rotation, cleanable: true) && dries)
            _dryingTracks.Enqueue((grid, decalId, _timing.CurTime + WaterTrackLifetime));
    }
}

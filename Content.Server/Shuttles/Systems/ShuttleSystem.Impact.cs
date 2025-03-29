using System.Numerics;
using Content.Server.Shuttles.Components;
using Robust.Server.GameObjects;
using Content.Shared.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Map;
using Robust.Shared.Physics.Events;
using Robust.Shared.Map.Components;
using Content.Shared.Damage;
using Content.Shared.Buckle.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Slippery;
using Content.Shared.Inventory;
using Content.Shared.Clothing;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;

namespace Content.Server.Shuttles.Systems;

public sealed partial class ShuttleSystem
{
    [Dependency] private readonly MapSystem _mapSys = default!;
    [Dependency] private readonly DamageableSystem _damageSys = default!;

    /// <summary>
    /// Minimum velocity difference between 2 bodies for a shuttle "impact" to occur.
    /// </summary>
    private const int MinimumImpactVelocity = 10;

    /// <summary>
    /// Kinetic energy required to dismantle a single tile
    /// </summary>
    private const float TileBreakEnergy = 5000;

    /// <summary>
    /// Kinetic energy required to spawn sparks
    /// </summary>
    private const float SparkEnergy = 7000;

    private readonly SoundCollectionSpecifier _shuttleImpactSound = new("ShuttleImpactSound");

    private void InitializeImpact()
    {
        SubscribeLocalEvent<ShuttleComponent, StartCollideEvent>(OnShuttleCollide);
    }

    private void OnShuttleCollide(EntityUid uid, ShuttleComponent component, ref StartCollideEvent args)
    {
        if (!TryComp<MapGridComponent>(uid, out var ourGrid) ||
            !TryComp<MapGridComponent>(args.OtherEntity, out var otherGrid))
            return;

        var ourBody = args.OurBody;
        var otherBody = args.OtherBody;

        // TODO: Would also be nice to have a continuous sound for scraping.
        var ourXform = Transform(uid);

        if (ourXform.MapUid == null)
            return;

        var otherXform = Transform(args.OtherEntity);

        var ourPoint = Vector2.Transform(args.WorldPoint, _transform.GetInvWorldMatrix(ourXform));
        var otherPoint = Vector2.Transform(args.WorldPoint, _transform.GetInvWorldMatrix(otherXform));

        var ourVelocity = _physics.GetLinearVelocity(uid, ourPoint, ourBody, ourXform);
        var otherVelocity = _physics.GetLinearVelocity(args.OtherEntity, otherPoint, otherBody, otherXform);
        var jungleDiff = (ourVelocity - otherVelocity).Length();

        if (jungleDiff < MinimumImpactVelocity)
            return;

        var energy = ourBody.Mass * Math.Pow(jungleDiff, 2) / 2;
        var dir = (ourVelocity.Length() > otherVelocity.Length() ? ourVelocity : -otherVelocity).Normalized();
        ProcessTile(uid, ourGrid, (Vector2i) ourPoint, (float) energy, -dir);
        ProcessTile(args.OtherEntity, otherGrid, (Vector2i) otherPoint, (float) energy, dir);

        var coordinates = new EntityCoordinates(ourXform.MapUid.Value, args.WorldPoint);
        var volume = MathF.Min(10f, 1f * MathF.Pow(jungleDiff, 0.5f) - 5f);
        var audioParams = AudioParams.Default.WithVariation(SharedContentAudioSystem.DefaultVariation).WithVolume(volume);
        _audio.PlayPvs(_shuttleImpactSound, coordinates, audioParams);
    }

    private void ProcessTile(EntityUid uid, MapGridComponent grid, Vector2i tile, float energy, Vector2 dir)
    {
        if (!TryComp<MapGridComponent>(gridUid, out var grid))
            return;

        // Find all entities on the grid
        var buckleQuery = GetEntityQuery<BuckleComponent>();
        var noSlipQuery = GetEntityQuery<NoSlipComponent>();
        var magbootsQuery = GetEntityQuery<MagbootsComponent>();
        var itemToggleQuery = GetEntityQuery<ItemToggleComponent>();
        var knockdownTime = TimeSpan.FromSeconds(5);

        // Get all entities with MobState component on the grid
        var query = EntityQueryEnumerator<MobStateComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out var mobState, out var xform))
        {
            // Skip entities not on this grid
            if (xform.GridUid != gridUid)
                continue;

            // If entity has a buckle component and is buckled, skip it
            if (buckleQuery.TryGetComponent(uid, out var buckle) && buckle.Buckled)
                continue;

            // Skip if the entity directly has NoSlip component
            if (noSlipQuery.HasComponent(uid))
                continue;

            // Check if the entity is wearing magboots
            bool hasMagboots = false;

            // Check if they're wearing shoes with NoSlip component or activated magboots
            if (_inventorySystem.TryGetSlotEntity(uid, "shoes", out var shoes))
            {
                // If shoes have NoSlip component
                if (noSlipQuery.HasComponent(shoes))
                {
                    hasMagboots = true;
                }
                // If shoes are magboots and they are activated
                else if (magbootsQuery.HasComponent(shoes) &&
                         itemToggleQuery.TryGetComponent(shoes, out var toggle) &&
                         toggle.Activated)
                {
                    hasMagboots = true;
                }
            }

            if (hasMagboots)
                continue;

            // Apply knockdown to unbuckled entities
            _stuns.TryKnockdown(uid, knockdownTime, true);
        }
    }

    /*/// <summary>
    /// Checks if a grid has any entities with GridShieldProtectedEntityComponent on it
    /// </summary>
    private bool IsGridProtected(EntityUid gridUid)
    {
        // If the grid itself has a protection component, it's protected
        if (HasComp<GridShieldProtectionComponent>(gridUid))
            return true;

        // Also check for any individual entities with protection
        var protectedQuery = GetEntityQuery<GridShieldProtectedEntityComponent>();
        var xformQuery = GetEntityQuery<TransformComponent>();

        // Check all entities with the protection component
        var query = EntityQueryEnumerator<GridShieldProtectedEntityComponent>();
        while (query.MoveNext(out var uid, out _))
        {
            if (xformQuery.TryGetComponent(uid, out var xform) && xform.GridUid == gridUid)
                return true;
        }

        return false;
    }*/

    /// <summary>
    /// Processes a zone of tiles around the impact point
    /// </summary>
    private void ProcessImpactZone(EntityUid uid, MapGridComponent grid, Vector2i centerTile, float energy, Vector2 dir, int radius)
    {
        // // Skip processing if this grid has entities protected by grid shields
        // if (IsGridProtected(uid))
        //     return;

        // Create damage object for entities
        DamageSpecifier damage = new();
        damage.DamageDict = new() { { "Blunt", energy } };

        foreach (EntityUid localUid in _lookup.GetLocalEntitiesIntersecting(uid, tile, gridComp: grid))
        {
            _damageSys.TryChangeDamage(localUid, damage);

            for (var y = -radius; y <= radius; y++)
            {
                // Skip tiles too far from impact center (creating a rough circle)
                if (x*x + y*y > radius*radius)
                    continue;

                Vector2i tile = new Vector2i(centerTile.X + x, centerTile.Y + y);

                // Calculate distance-based energy falloff
                float distanceFactor = 1.0f - (float)Math.Sqrt(x*x + y*y) / (radius + 1);
                float tileEnergy = energy * distanceFactor;

                // Process entities on this tile
                foreach (EntityUid localUid in _lookup.GetLocalEntitiesIntersecting(uid, tile, gridComp: grid))
                {
                    // Skip entities protected by grid shields or entities that no longer exist
                    if (!Exists(localUid))
                        continue;

                    // Scale damage based on distance from center
                    var scaledDamage = new DamageSpecifier(damage) { DamageDict = { ["Blunt"] = tileEnergy } };
                    _damageSys.TryChangeDamage(localUid, scaledDamage);

                    // Check if the entity has a transform component before proceeding
                    if (!TryComp<TransformComponent>(localUid, out var form))
                        continue;

                    if (!form.Anchored)
                        _transform.Unanchor(localUid, form);

                    // Safely try to throw the entity
                    if (Exists(localUid))
                        _throwing.TryThrow(localUid, dir * distanceFactor); // Reduce throw force based on distance
                }

                // Only break tiles if they have enough energy
                if (tileEnergy > TileBreakEnergy)
                    _mapSys.SetTile(new Entity<MapGridComponent>(uid, grid), tile, Tile.Empty);

                // Spawn sparks with probability based on energy
        }

        if (energy > TileBreakEnergy)
            _mapSys.SetTile(new Entity<MapGridComponent>(uid, grid), tile, Tile.Empty);

        if (energy > SparkEnergy)
            Spawn("EffectSparks", new EntityCoordinates(uid, tile));
    }
}

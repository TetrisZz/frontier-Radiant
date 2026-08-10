using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Item.ItemToggle.Components;

namespace Content.Server._radiant.Shower;

public sealed class ShowerWaterSystem : EntitySystem
{
    [Dependency] private readonly PuddleSystem _puddle = default!;

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShowerWaterComponent, ItemToggleComponent, TransformComponent>();
        while (query.MoveNext(out _, out var shower, out var toggle, out var transform))
        {
            if (!toggle.Activated)
            {
                shower.Accumulator = 0f;
                continue;
            }

            shower.Accumulator += frameTime;
            if (shower.Accumulator < shower.SpillInterval)
                continue;

            shower.Accumulator %= shower.SpillInterval;
            var direction = transform.LocalRotation.ToWorldVec();

            for (var distance = 1; distance <= shower.SpillTiles; distance++)
            {
                var coordinates = transform.Coordinates.Offset(direction * distance);
                var solution = new Solution(shower.Reagent, FixedPoint2.New(shower.WaterPerTile));
                _puddle.TrySpillAt(coordinates, solution, out _, sound: false);
            }
        }
    }
}

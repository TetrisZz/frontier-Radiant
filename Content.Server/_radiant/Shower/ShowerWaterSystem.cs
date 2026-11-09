using Content.Server._radiant.Plumbing;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;

namespace Content.Server._radiant.Shower;

public sealed partial class ShowerWaterSystem : EntitySystem
{
    [Dependency] private PuddleSystem _puddle = default!;
    [Dependency] private PlumbingSystem _plumbing = default!;
    [Dependency] private ItemToggleSystem _toggle = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ShowerWaterComponent, ItemToggleActivateAttemptEvent>(OnActivateAttempt);
    }

    private void OnActivateAttempt(Entity<ShowerWaterComponent> ent, ref ItemToggleActivateAttemptEvent args)
    {
        if (_plumbing.AvailableWater(ent) > 0)
            return;

        args.Cancelled = true;
        args.Popup = Loc.GetString("plumbing-no-water");
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<ShowerWaterComponent, ItemToggleComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var shower, out var toggle, out var transform))
        {
            if (!toggle.Activated)
            {
                shower.Accumulator = 0f;
                continue;
            }

            if (_plumbing.AvailableWater(uid) <= 0)
            {
                _toggle.TrySetActive((uid, toggle), false, predicted: false);
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
                var amount = _plumbing.DrawWater(uid, FixedPoint2.New(shower.WaterPerTile));
                if (amount <= 0)
                {
                    _toggle.TrySetActive((uid, toggle), false, predicted: false);
                    break;
                }

                var solution = new Solution(shower.Reagent, amount);
                _puddle.TrySpillAt(coordinates, solution, out _, sound: false);
                var ev = new ShowerSprayEvent(uid, coordinates, shower.Reagent.Id,
                    (float) amount, shower.SpillTiles + 0.5f);
                RaiseLocalEvent(ref ev);
            }
        }
    }
}

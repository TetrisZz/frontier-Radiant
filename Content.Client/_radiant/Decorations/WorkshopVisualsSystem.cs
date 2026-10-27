using System.Linq;
using System.Numerics;
using Content.Shared._radiant.Decorations;
using Robust.Client.GameObjects;
using Robust.Shared.Timing;

namespace Content.Client._radiant.Decorations;

public sealed class WorkshopVisualsSystem : EntitySystem
{
    [Dependency] private readonly AppearanceSystem _appearance = default!;
    [Dependency] private readonly SpriteSystem _sprites = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<AquariumComponent, AppearanceChangeEvent>(OnAquariumChanged);
        SubscribeLocalEvent<SurgicalCartVisualsComponent, AppearanceChangeEvent>(OnCartChanged);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<AquariumComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out var aquarium, out var sprite))
        {
            if (!_appearance.TryGetData<AquariumFishVisualState>(uid, AquariumVisuals.Fish, out var fish))
                continue;
            for (var i = 0; i < Math.Min(4, aquarium.FishOffsets.Length); i++)
            {
                if (i >= fish.Alive.Length || !fish.Alive[i])
                    continue;
                var phase = (float)_timing.CurTime.TotalSeconds * 0.7f + i * 1.7f + uid.GetHashCode() % 31;
                var offset = aquarium.FishOffsets[i] + new Vector2(MathF.Sin(phase) * 0.025f,
                    MathF.Sin(phase * 0.8f) * 0.018f);
                _sprites.LayerSetOffset((uid, sprite), "fish" + i, offset);
            }
        }
    }

    private void OnAquariumChanged(Entity<AquariumComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null)
            return;
        if (_appearance.TryGetData<AquariumFishVisualState>(ent, AquariumVisuals.Fish, out var fish, args.Component))
        {
            for (var i = 0; i < 4; i++)
            {
                var layer = "fish" + i;
                _sprites.LayerSetVisible((ent.Owner, args.Sprite), layer, i < fish.States.Length);
                if (i < fish.States.Length)
                {
                    _sprites.LayerSetRsiState((ent.Owner, args.Sprite), layer, fish.States[i]);
                    _sprites.LayerSetColor((ent.Owner, args.Sprite), layer, i < fish.Colors.Length ? fish.Colors[i] : Color.White);
                    _sprites.LayerSetScale((ent.Owner, args.Sprite), layer, new Vector2(i < fish.Juveniles.Length && fish.Juveniles[i] ? 0.65f : 1f));
                }
            }
        }
        if (_appearance.TryGetData<string[]>(ent, AquariumVisuals.Decorations, out var decorations, args.Component))
            foreach (var state in new[] { "rocks", "seaweeds_front", "treasure", "sand" })
                _sprites.LayerSetVisible((ent.Owner, args.Sprite), state, decorations.Contains(state));
        if (_appearance.TryGetData<bool>(ent, AquariumVisuals.Water, out var water, args.Component))
            _sprites.LayerSetVisible((ent.Owner, args.Sprite), "water", water);
    }

    private void OnCartChanged(Entity<SurgicalCartVisualsComponent> ent, ref AppearanceChangeEvent args)
    {
        if (args.Sprite == null || !_appearance.TryGetData<string[]>(ent, SurgicalCartVisuals.Tools, out var states, args.Component))
            return;
        foreach (var state in ent.Comp.ToolStates.Values.Distinct())
            _sprites.LayerSetVisible((ent.Owner, args.Sprite), state, states.Contains(state));
    }
}

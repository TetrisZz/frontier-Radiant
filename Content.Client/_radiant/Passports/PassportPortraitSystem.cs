using Robust.Client.GameObjects;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Client._radiant.Passports;

[RegisterComponent]
public sealed partial class PassportPortraitComponent : Component;

/// <summary>Keep document photographs still, including markings rebuilt after equipment changes.</summary>
public sealed class PassportPortraitSystem : EntitySystem
{
    [Dependency] private SpriteSystem _sprites = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void FrameUpdate(float frameTime)
    {
        var query = EntityQueryEnumerator<PassportPortraitComponent, SpriteComponent>();
        while (query.MoveNext(out var uid, out _, out var sprite))
            Prepare(uid);
    }

    public void Prepare(EntityUid uid)
    {
        if (!TryComp<SpriteComponent>(uid, out var sprite))
            return;

        // A base tail layer can already be hidden while its separate marking layers remain visible.
        if (TryComp<HumanoidAppearanceComponent>(uid, out var humanoid))
        {
            foreach (var category in humanoid.MarkingSet.Markings.Values)
            foreach (var marking in category)
            {
                if (!_prototypes.TryIndex<MarkingPrototype>(marking.MarkingId, out var prototype)
                    || prototype.BodyPart is not (HumanoidVisualLayers.Tail or HumanoidVisualLayers.Wings))
                    continue;
                foreach (var specifier in prototype.Sprites)
                {
                    if (specifier is SpriteSpecifier.Rsi rsi
                        && _sprites.LayerMapTryGet((uid, sprite), $"{prototype.ID}-{rsi.RsiState}", out var layer, false))
                        _sprites.LayerSetVisible((uid, sprite), layer, false);
                }
            }
        }

        var index = 0;
        foreach (var layer in sprite.AllLayers)
        {
            _sprites.LayerSetAutoAnimated((uid, sprite), index, false);
            _sprites.LayerSetAnimationTime((uid, sprite), index, 0);
            index++;
        }
    }
}

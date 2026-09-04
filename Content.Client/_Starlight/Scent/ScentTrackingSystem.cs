using Content.Shared._Starlight.Scent.Components;
using Robust.Client.GameObjects;
using Robust.Client.Player;
using System.Globalization;
using System.Numerics;

namespace Content.Client._Starlight.Scent;

/// <summary>
/// Filters scent markers to the trail selected by the local player.
/// </summary>
public sealed class ScentTrackingSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _player = default!;
    [Dependency] private readonly SpriteSystem _sprite = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ScentMarkerComponent, ComponentStartup>(OnMarkerChanged);
        SubscribeLocalEvent<ScentMarkerComponent, AfterAutoHandleStateEvent>(OnMarkerState);
        SubscribeLocalEvent<SmellerComponent, AfterAutoHandleStateEvent>(OnSmellerState);
    }

    private void OnMarkerChanged(Entity<ScentMarkerComponent> ent, ref ComponentStartup args) => Apply(ent);
    private void OnMarkerState(Entity<ScentMarkerComponent> ent, ref AfterAutoHandleStateEvent args) => Apply(ent);

    private void OnSmellerState(Entity<SmellerComponent> ent, ref AfterAutoHandleStateEvent args)
    {
        if (_player.LocalEntity != ent.Owner)
            return;

        var query = EntityQueryEnumerator<ScentMarkerComponent>();
        while (query.MoveNext(out var uid, out var marker))
            Apply((uid, marker));
    }

    private void Apply(Entity<ScentMarkerComponent> ent)
    {
        var visible = false;
        if (_player.LocalEntity is { } local && TryComp<SmellerComponent>(local, out var smeller))
        {
            visible = smeller.Sniffing &&
                      (smeller.TrackedScentId == null || smeller.TrackedScentId == ent.Comp.ScentId);
        }

        if (TryComp<SpriteComponent>(ent.Owner, out var sprite))
        {
            _sprite.SetVisible((ent.Owner, sprite), visible);
            _sprite.SetScale((ent.Owner, sprite), new Vector2(0.34f));
            _sprite.SetColor((ent.Owner, sprite), GetScentColor(ent.Comp.ScentId));
        }
    }

    // Radiant sector: stable per-scent colouring makes overlapping trails distinguishable.
    private static Color GetScentColor(string scentId)
    {
        if (scentId.Length < 8 || !uint.TryParse(scentId[..8], NumberStyles.HexNumber, null, out var seed))
            return Color.White.WithAlpha(0.45f);

        var hue = (seed % 360) / 360f;
        return Color.FromHsv(new Vector4(hue, 0.75f, 1f, 0.45f));
    }
}

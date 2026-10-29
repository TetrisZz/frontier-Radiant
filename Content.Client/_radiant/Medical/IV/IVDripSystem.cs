using System.Numerics;
using System.Linq;
using Content.Shared._radiant.Medical.IV;
using Robust.Client.GameObjects;
using Robust.Client.Graphics;
using Robust.Shared.Enums;

namespace Content.Client._radiant.Medical.IV;

public sealed class IVDripSystem : SharedIVDripSystem
{
    [Dependency] private readonly IOverlayManager _overlays = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<IVDripComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<IVDripComponent, AfterAutoHandleStateEvent>(OnState);
        _overlays.AddOverlay(new IVDripOverlay(EntityManager));
    }

    public override void Shutdown()
    {
        _overlays.RemoveOverlay<IVDripOverlay>();
        base.Shutdown();
    }

    private void OnStartup(Entity<IVDripComponent> ent, ref ComponentStartup args) => UpdateVisuals(ent);
    private void OnState(Entity<IVDripComponent> ent, ref AfterAutoHandleStateEvent args) => UpdateVisuals(ent);

    private void UpdateVisuals(Entity<IVDripComponent> ent)
    {
        if (!TryComp<SpriteComponent>(ent, out var sprite))
            return;
        var suffix = ent.Comp.Telescopic ? "_tele" : "";
        var state = ent.Comp.Telescopic ? "iv_drip_tele" : "iv_drip";
        if (ent.Comp.HasBag)
            state = (ent.Comp.Injecting
                ? ent.Comp.Flowing ? "injecting" : "injectidle"
                : ent.Comp.Flowing ? "donating" : "donateidle") + suffix;
        sprite.LayerSetState(IVDripVisuals.Stand, state);
        sprite.LayerSetVisible(IVDripVisuals.Liquid, ent.Comp.HasBag);
        if (!ent.Comp.HasBag)
            return;
        var level = new[] { 0, 10, 25, 50, 75, 80, 100 }.Last(value => value <= ent.Comp.FillPercentage);
        sprite.LayerSetState(IVDripVisuals.Liquid, "reagent" + level);
        sprite.LayerSetColor(IVDripVisuals.Liquid, ent.Comp.FillColor);
    }
}

public sealed class IVDripOverlay(IEntityManager entities) : Overlay
{
    public override OverlaySpace Space => OverlaySpace.WorldSpaceBelowEntities;

    protected override void Draw(in OverlayDrawArgs args)
    {
        var transforms = entities.System<SharedTransformSystem>();
        var query = entities.EntityQueryEnumerator<IVDripComponent>();
        while (query.MoveNext(out var stand, out var iv))
        {
            if (iv.AttachedTo is not { } patient || !entities.EntityExists(patient))
                continue;
            var from = transforms.GetMapCoordinates(stand);
            var to = transforms.GetMapCoordinates(patient);
            if (from.MapId != args.MapId || to.MapId != args.MapId || !from.InRange(to, iv.Range))
                continue;
            args.WorldHandle.DrawLine(from.Position + new Vector2(0.12f, 0.35f),
                to.Position + new Vector2(0.1f, 0.1f), Color.FromHex("#B7DDE0"));
        }
    }
}

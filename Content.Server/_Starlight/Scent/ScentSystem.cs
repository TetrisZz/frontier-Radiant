using Content.Shared._Starlight.Scent;
using Content.Shared._Starlight.Scent.Components;
using Content.Shared._Starlight.Scent.Systems;
using Content.Shared.Eye;
using Content.Shared.Popups;
using Robust.Shared.Map;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Scent;

/// <summary>
/// Emits personal scent trails and lets an olfactory implant select one to follow.
/// </summary>
public sealed class ScentSystem : SharedScentSystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ScentComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SmellerComponent, GetVisMaskEvent>(OnGetVisMask);
        SubscribeLocalEvent<SmellerComponent, TrackScentActionEvent>(OnTrackScent);
    }

    private void OnMapInit(Entity<ScentComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.ScentId ??= Guid.NewGuid().ToString("N");
        ent.Comp.NextEmit = _timing.CurTime;
        Dirty(ent);
    }

    private void OnGetVisMask(Entity<SmellerComponent> ent, ref GetVisMaskEvent args)
    {
        if (ent.Comp.Sniffing)
            args.VisibilityMask |= (int) VisibilityFlags.Scent;
    }

    private void OnTrackScent(Entity<SmellerComponent> ent, ref TrackScentActionEvent args)
    {
        if (args.Handled)
            return;

        if (!TryComp<ScentComponent>(args.Target, out var scent) || scent.ScentId == null)
        {
            _popup.PopupEntity(Loc.GetString("scent-track-no-scent"), args.Target, ent.Owner);
            args.Handled = true;
            return;
        }

        SetTrackedScent(ent, scent.ScentId);
        _popup.PopupEntity(Loc.GetString("scent-track-success", ("target", args.Target)), ent.Owner, ent.Owner);
        args.Handled = true;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<ScentComponent, TransformComponent>();
        while (query.MoveNext(out _, out var scent, out var xform))
        {
            if (scent.ScentId == null || _timing.CurTime < scent.NextEmit || xform.MapID == MapId.Nullspace)
                continue;

            scent.NextEmit = _timing.CurTime + scent.EmitInterval;
            var marker = Spawn("ScentMarker", xform.Coordinates);
            var markerComp = Comp<ScentMarkerComponent>(marker);
            markerComp.ScentId = scent.ScentId;
            Dirty(marker, markerComp);
        }
    }
}

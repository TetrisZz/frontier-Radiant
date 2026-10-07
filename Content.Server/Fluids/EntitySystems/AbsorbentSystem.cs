using System;
using System.Linq;
using Content.Server.Decals;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Interaction;
using Content.Shared.Maps;
using Content.Shared.Popups;
using Content.Shared.Timing;
using Robust.Shared.Audio.Systems;

namespace Content.Server.Fluids.EntitySystems;

/// <inheritdoc/>
public sealed partial class AbsorbentSystem : SharedAbsorbentSystem
{
    private static readonly FixedPoint2 TraceCleanCost = FixedPoint2.New(0.25f);

    [Dependency] private DecalSystem _decals = default!;
    [Dependency] private TurfSystem _turf = default!;
    [Dependency] private UseDelaySystem _useDelay = default!;
    [Dependency] private SharedPopupSystem _popups = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    protected override void OnAfterInteract(Entity<AbsorbentComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Target != null)
        {
            base.OnAfterInteract(ent, ref args);
            return;
        }

        if (!args.CanReach || args.Handled)
            return;

        var tile = _turf.GetTileRef(args.ClickLocation);
        if (tile == null || tile.Value.Tile.IsEmpty)
            return;

        if (TryComp<UseDelayComponent>(ent, out var delay) && _useDelay.IsDelayed((ent.Owner, delay)))
            return;

        var grid = tile.Value.GridUid;
        var tilePos = tile.Value.GridIndices;
        // The traces are offset around the tile origin and may even cross a decal
        // chunk boundary. A radius query only checks one chunk and misses edge lanes.
        var bounds = new Box2(tilePos.X - 0.5f, tilePos.Y - 0.5f,
            tilePos.X + 0.5f, tilePos.Y + 0.5f);
        var traces = _decals.GetDecalsIntersecting(grid, bounds)
            .Where(trace => trace.Decal.Cleanable && IsTrace(trace.Decal.Id))
            .ToArray();
        if (traces.Length == 0 || !SolutionContainer.TryGetSolution(ent.Owner, ent.Comp.SolutionName, out var absorbent))
            return;

        var solution = absorbent.Value.Comp.Solution;
        var reagents = Puddle.GetAbsorbentReagents(solution);
        var available = solution.GetTotalPrototypeQuantity(reagents);
        if (ent.Comp.UseAbsorberSolution && available < TraceCleanCost)
        {
            _popups.PopupClient(Loc.GetString("mopping-system-no-water", ("used", ent)), ent, args.User);
            args.Handled = true;
            return;
        }

        var cleaned = 0;
        foreach (var (index, _) in traces)
        {
            if (ent.Comp.UseAbsorberSolution && available < TraceCleanCost)
                break;

            if (!_decals.RemoveDecal(grid, index))
                continue;

            cleaned++;
            if (ent.Comp.UseAbsorberSolution)
                available -= TraceCleanCost;
        }

        if (cleaned == 0)
            return;

        if (ent.Comp.UseAbsorberSolution)
        {
            solution.SplitSolutionWithOnly(TraceCleanCost * cleaned, reagents);
            SolutionContainer.UpdateChemicals(absorbent.Value);
        }

        _audio.PlayPredicted(ent.Comp.PickupSound, ent, args.User);
        if (delay != null)
            _useDelay.TryResetDelay((ent.Owner, delay));
        args.Handled = true;
    }

    private static bool IsTrace(string id) => id == "footprint" ||
        id.StartsWith("RadiantFootstep", StringComparison.Ordinal) ||
        id.StartsWith("RadiantCrawlTrail", StringComparison.Ordinal) ||
        id.StartsWith("RadiantBloodCrawlTrail", StringComparison.Ordinal) ||
        id.StartsWith("RadiantBloodSplatter", StringComparison.Ordinal);
}

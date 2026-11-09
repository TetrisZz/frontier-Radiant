using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.NodeContainer;
using Robust.Shared.Map.Components;

namespace Content.Server._radiant.Plumbing;

public sealed partial class LiquidPipeNode : Node, IRotatableNode
{
    /// <summary>Fixtures and tanks connect to a pipe on their own tile.</summary>
    [DataField]
    public bool Port;

    [DataField]
    public PipeDirection PipeDirection = PipeDirection.Fourway;

    bool IRotatableNode.RotateNode(in MoveEvent ev)
        => !Port && PipeDirection.RotatePipeDirection(ev.OldRotation) != PipeDirection.RotatePipeDirection(ev.NewRotation);

    public override IEnumerable<Node> GetReachableNodes(
        Entity<TransformComponent> xform,
        EntityQuery<NodeContainerComponent> nodeQuery,
        EntityQuery<TransformComponent> xformQuery,
        Entity<MapGridComponent>? grid,
        IEntityManager entMan)
    {
        if (!xform.Comp.Anchored || grid is not { } gridEnt)
            yield break;

        var map = entMan.System<SharedMapSystem>();
        var tile = map.TileIndicesFor(gridEnt, xform.Comp.Coordinates);
        var openings = PipeDirection.RotatePipeDirection(xform.Comp.LocalRotation);
        foreach (var (direction, other) in NodeHelpers.GetCardinalNeighborNodes(nodeQuery, gridEnt, tile, map))
        {
            if (other is not LiquidPipeNode pipe || pipe.NodeGroupID != NodeGroupID)
                continue;

            if (direction == Direction.Invalid)
            {
                if (Port != pipe.Port)
                    yield return pipe;
                continue;
            }

            var side = direction.ToPipeDirection();
            if (!Port && !pipe.Port && openings.HasDirection(side) &&
                xformQuery.TryGetComponent(pipe.Owner, out var neighbor) &&
                pipe.PipeDirection.RotatePipeDirection(neighbor.LocalRotation).HasDirection(side.GetOpposite()))
                yield return pipe;
        }
    }
}

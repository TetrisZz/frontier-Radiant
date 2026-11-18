using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.NodeGroups;
using Content.Server.NodeContainer.Nodes;
using Content.Shared.Atmos;
using Content.Shared.NodeContainer;

namespace Content.Server._Triad.Shipyard;

/// <summary>Snapshot-only gas shares, restored once the loaded pipe networks have formed.</summary>
[RegisterComponent]
public sealed partial class ShipSavedPipeGasComponent : Component
{
    [DataField]
    public Dictionary<string, GasMixture> Mixtures = new();
}

public sealed class ShipSavedPipeGasSystem : EntitySystem
{
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(NodeGroupSystem));
        UpdatesBefore.Add(typeof(AtmosphereSystem));
    }

    public Dictionary<string, GasMixture> Capture(EntityUid uid, NodeContainerComponent nodes)
    {
        var result = new Dictionary<string, GasMixture>();
        // A ship can be exported again before its first update after loading.
        if (TryComp<ShipSavedPipeGasComponent>(uid, out var pending))
        {
            foreach (var (name, mixture) in pending.Mixtures)
                result[name] = mixture.Clone();
        }

        foreach (var (name, node) in nodes.Nodes)
        {
            if (node is not PipeNode pipe || pipe.NodeGroup is not IPipeNet net || net.Air.Volume <= 0)
                continue;

            // Each node exports only its share, including when a network crosses the saved grid's boundary.
            var share = net.Air.Clone();
            share.Multiply(pipe.Volume / net.Air.Volume);
            share.Volume = pipe.Volume;
            if (result.TryGetValue(name, out var existing))
                _atmosphere.Merge(existing, share);
            else
                result[name] = share;
        }

        return result;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ShipSavedPipeGasComponent, NodeContainerComponent>();
        while (query.MoveNext(out var uid, out var saved, out var nodes))
        {
            if (MetaData(uid).EntityPaused)
                continue;

            foreach (var (name, mixture) in saved.Mixtures.ToArray())
            {
                if (!nodes.Nodes.TryGetValue(name, out var node) || node is not PipeNode pipe)
                {
                    saved.Mixtures.Remove(name);
                    continue;
                }
                if (pipe.NodeGroup is not IPipeNet net)
                    continue;

                _atmosphere.Merge(net.Air, mixture);
                saved.Mixtures.Remove(name);
            }

            if (saved.Mixtures.Count == 0)
                RemCompDeferred<ShipSavedPipeGasComponent>(uid);
        }
    }
}

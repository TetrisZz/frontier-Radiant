using System.Linq;
using Content.Shared._radiant.Decorations;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Decorations;

public sealed class SurgicalCartSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SurgicalCartVisualsComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<SurgicalCartVisualsComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SurgicalCartVisualsComponent, EntRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnInit(Entity<SurgicalCartVisualsComponent> ent, ref MapInitEvent args) => UpdateContents(ent);
    private void OnInserted(Entity<SurgicalCartVisualsComponent> ent, ref EntInsertedIntoContainerMessage args) => UpdateContents(ent);
    private void OnRemoved(Entity<SurgicalCartVisualsComponent> ent, ref EntRemovedFromContainerMessage args) => UpdateContents(ent);

    public void UpdateContents(Entity<SurgicalCartVisualsComponent> ent)
    {
        var states = new HashSet<string>();
        if (_containers.TryGetContainer(ent, "storagebase", out var container))
        foreach (var item in container.ContainedEntities)
        {
            var proto = MetaData(item).EntityPrototype;
            var queue = new Queue<EntityPrototype>();
            if (proto != null)
                queue.Enqueue(proto);
            while (queue.TryDequeue(out proto))
            {
                if (ent.Comp.ToolStates.TryGetValue(proto.ID, out var state))
                {
                    states.Add(state);
                    break;
                }
                if (proto.Parents is { } parents)
                    foreach (var parent in parents)
                        queue.Enqueue(_prototypes.Index<EntityPrototype>(parent));
            }
        }
        _appearance.SetData(ent, SurgicalCartVisuals.Tools, states.OrderBy(s => s).ToArray());
    }
}

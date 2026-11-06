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
            if (MetaData(item).EntityPrototype is not { } proto)
                continue;

            foreach (var (id, _) in _prototypes.EnumerateAllParents<EntityPrototype>(proto.ID, includeSelf: true))
            {
                if (!ent.Comp.ToolStates.TryGetValue(id, out var state))
                    continue;

                states.Add(state);
                break;
            }
        }
        _appearance.SetData(ent, SurgicalCartVisuals.Tools, states.OrderBy(s => s).ToArray());
    }
}

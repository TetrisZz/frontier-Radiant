using Content.Shared._radiant.SmartFridge;
using Robust.Shared.Containers;

namespace Content.Server._radiant.SmartFridge;

public sealed class SmartFridgeFillSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<SmartFridgeFillVisualsComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<SmartFridgeFillVisualsComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SmartFridgeFillVisualsComponent, EntRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnMapInit(Entity<SmartFridgeFillVisualsComponent> ent, ref MapInitEvent args) => UpdateFill(ent);

    private void OnInserted(Entity<SmartFridgeFillVisualsComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.Container)
            UpdateFill(ent);
    }

    private void OnRemoved(Entity<SmartFridgeFillVisualsComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.Container)
            UpdateFill(ent);
    }

    private void UpdateFill(Entity<SmartFridgeFillVisualsComponent> ent)
    {
        if (_containers.TryGetContainer(ent, ent.Comp.Container, out var container))
            _appearance.SetData(ent, SmartFridgeFillVisuals.Level,
                SmartFridgeFillVisualsComponent.FillLevel(container.ContainedEntities.Count));
    }
}

using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared._radiant.Chemistry;
using Robust.Shared.GameObjects;

namespace Content.Server._radiant.Chemistry.Systems;

public sealed class RadiantMetamorphicGlassSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<RadiantMetamorphicGlassComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
    }

    private void OnSolutionChanged(Entity<RadiantMetamorphicGlassComponent> entity, ref SolutionContainerChangedEvent args)
    {
        if (args.Solution.Volume > 0)
            return;

        _appearance.RemoveData(entity.Owner, SolutionContainerVisuals.BaseOverride);
    }
}

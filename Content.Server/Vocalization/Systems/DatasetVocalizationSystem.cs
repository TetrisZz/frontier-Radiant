using Content.Server.Vocalization.Components;
using Content.Shared.Random.Helpers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Vocalization.Systems;

/// <inheritdoc cref="DatasetVocalizerComponent"/>
public sealed partial class DatasetVocalizationSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _protoMan = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<DatasetVocalizerComponent, TryVocalizeEvent>(OnTryVocalize);
    }

    private void OnTryVocalize(Entity<DatasetVocalizerComponent> ent, ref TryVocalizeEvent args)
    {
        if (args.Handled)
            return;

        if (!_random.Prob(ent.Comp.Chance))
            return;

        // Radiant sector start - do not crash the entity-system tick on an empty dataset.
        if (ent.Comp.Dataset is not { } datasetId || !_protoMan.TryIndex(datasetId, out var dataset))
            return;
        // Radiant sector end

        args.Message = _random.Pick(dataset);
        args.Handled = true;
    }
}

using System.Linq;
using Content.Shared._radiant.Medical.Genetics;

namespace Content.Server._radiant.Medical.Genetics;

public sealed partial class GeneticBodySystem
{
    private GeneticSampleComponent? AdminSample(EntityUid target)
        => CompOrNull<GeneticSampleComponent>(target)
            ?? (TryComp<GeneticBodyGrowerComponent>(target, out var machine) ? ActiveSample(machine) : null);

    public bool TryGetResearchSequence(EntityUid target, string gene, out string sequence)
    {
        sequence = "";
        if (!_prototypes.HasIndex<GeneticModificationPrototype>(gene)
            || AdminSample(target) is not { Species: not null } sample)
            return false;
        sequence = Sequence(sample, gene);
        return true;
    }

    public bool TrySetResearchSequence(EntityUid target, string gene, string sequence)
    {
        if (!_prototypes.HasIndex<GeneticModificationPrototype>(gene) || sequence.Length != 6
            || sequence.Any(c => !"ATGC".Contains(c)) || AdminSample(target) is not { Species: not null } sample)
            return false;
        var key = ResearchKey(sample);
        var busy = EntityQueryEnumerator<GeneticBodyGrowerComponent>();
        while (busy.MoveNext(out _, out var machine))
            if (machine.Stage != GeneticGrowthStage.Idle && ActiveSample(machine) is { } current && ResearchKey(current) == key)
                return false;
        var research = ResearchFor(sample);
        research.Sequences[gene] = sequence;
        research.Evidence.Remove(gene);
        research.Decoded.Remove(gene);
        research.History.Remove(gene);
        research.Spectra.Remove(gene);
        var machines = EntityQueryEnumerator<GeneticBodyGrowerComponent>();
        while (machines.MoveNext(out var uid, out var machine))
        {
            if (ActiveSample(machine) is not { } current || ResearchKey(current) != key)
                continue;
            if (GeneFor(machine.Product) == gene)
                machine.Product = GeneticProduct.Body;
            PublishUi(uid);
        }
        return true;
    }
}

using System.Linq;
using Content.Shared._radiant.Medical.Genetics;
using Content.Shared.GameTicking;
using Content.Shared.Examine;
using Robust.Shared.Random;

namespace Content.Server._radiant.Medical.Genetics;

[RegisterComponent]
public sealed partial class GeneticResearchDiskComponent : Component
{
    // General scientific knowledge, never a patient's sequence or progress.
    public HashSet<string> Discoveries = new();
}

/// <summary>Round-local research for one biological identity, shared by all of its samples.</summary>
public sealed class GeneticDonorResearch
{
    public Dictionary<string, string> Sequences = new();
    public Dictionary<string, string> Evidence = new();
    public Dictionary<string, string> History = new();
    public HashSet<string> Decoded = new();
    public HashSet<string> Spectra = new();
}

public sealed partial class GeneticBodySystem
{
    [Dependency] private IRobustRandom _geneticRandom = default!;
    private readonly Dictionary<string, GeneticDonorResearch> _donorResearch = new();

    private void InitializeDiscovery()
    {
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnGeneticsRestart);
        SubscribeLocalEvent<GeneticResearchDiskComponent, ExaminedEvent>(OnResearchDiskExamine);
    }

    private void OnResearchDiskExamine(Entity<GeneticResearchDiskComponent> ent, ref ExaminedEvent args)
        => args.PushText(Loc.GetString("genetics-disk-records", ("count", ent.Comp.Discoveries.Count)));

    private static string ResearchKey(GeneticSampleComponent sample) => sample.Species + "\n" + sample.Dna;

    private GeneticDonorResearch ResearchFor(GeneticSampleComponent sample)
    {
        var key = ResearchKey(sample);
        if (!_donorResearch.TryGetValue(key, out var research))
        {
            research = new GeneticDonorResearch();
            _donorResearch.Add(key, research);
        }
        return research;
    }

    private string Sequence(GeneticSampleComponent sample, string gene)
    {
        var research = ResearchFor(sample);
        if (research.Sequences.TryGetValue(gene, out var stored))
            return stored;
        string sequence;
        var attempts = 0;
        do
        {
            sequence = new string(Enumerable.Range(0, 6).Select(_ => "ATGC"[_geneticRandom.Next(4)]).ToArray());
        } while (++attempts < 64 && _donorResearch.Values.Any(r => r.Sequences.GetValueOrDefault(gene) == sequence));
        research.Sequences.Add(gene, sequence);
        return sequence;
    }

    private void OnGeneticsRestart(RoundRestartCleanupEvent args)
    {
        _donorResearch.Clear();
        var disks = EntityQueryEnumerator<GeneticResearchDiskComponent>();
        while (disks.MoveNext(out _, out var disk))
            disk.Discoveries.Clear();
        var machines = EntityQueryEnumerator<GeneticBodyGrowerComponent>();
        while (machines.MoveNext(out _, out var machine))
        {
            machine.Discoveries.Clear();
            Reset(machine);
        }
    }

    private bool HasNewEvidence(GeneticBodyGrowerComponent machine, GeneticSampleComponent sample)
    {
        var research = ResearchFor(sample);
        return _prototypes.HasIndex<GeneticModificationPrototype>(machine.SelectedGene)
            && !research.Decoded.Contains(machine.SelectedGene)
            && research.Evidence.GetValueOrDefault(machine.SelectedGene, "??????").Contains('?');
    }

    private void StudySample(GeneticBodyGrowerComponent machine, GeneticSampleComponent sample)
    {
        var research = ResearchFor(sample);
        var gene = machine.SelectedGene;
        var answer = Sequence(sample, gene);
        var evidence = research.Evidence.GetValueOrDefault(gene, "??????").ToCharArray();
        var unknown = Enumerable.Range(0, 6).Where(i => evidence[i] == '?').ToList();
        for (var count = 0; count < 2 && unknown.Count > 0; count++)
        {
            var index = _geneticRandom.Next(unknown.Count);
            var site = unknown[index];
            evidence[site] = answer[site];
            unknown.RemoveAt(index);
        }
        research.Evidence[gene] = new string(evidence);
    }

    public bool TrySequence(EntityUid uid, string guess)
    {
        var machine = Comp<GeneticBodyGrowerComponent>(uid);
        if (machine.Stage != GeneticGrowthStage.Idle || !Powered(uid)
            || machine.Sample.ContainedEntity is not { } cartridge
            || !TryComp<GeneticSampleComponent>(cartridge, out var sample) || !sample.Analyzed || !sample.TherapyCompatible
            || !_prototypes.HasIndex<GeneticModificationPrototype>(machine.SelectedGene)
            || string.IsNullOrEmpty(guess) || guess.Length != 6 || guess.Any(c => !"ATGC".Contains(c)))
            return false;
        var research = ResearchFor(sample);
        if (!research.Evidence.ContainsKey(machine.SelectedGene) || research.Decoded.Contains(machine.SelectedGene))
            return false;
        machine.PendingSequence = guess;
        machine.Stage = GeneticGrowthStage.Sequencing;
        machine.Remaining = 10;
        machine.PowerFailure = 0;
        return true;
    }

    public bool TrySpectrum(EntityUid uid)
    {
        var machine = Comp<GeneticBodyGrowerComponent>(uid);
        if (machine.Stage != GeneticGrowthStage.Idle || !Powered(uid)
            || ActiveSample(machine) is not { Analyzed: true, TherapyCompatible: true } sample
            || !_prototypes.HasIndex<GeneticModificationPrototype>(machine.SelectedGene)
            || ResearchFor(sample).Spectra.Contains(machine.SelectedGene))
            return false;
        machine.Stage = GeneticGrowthStage.Spectrometry;
        machine.Remaining = 30;
        machine.PowerFailure = 0;
        return true;
    }

    private void FinishSequence(GeneticBodyGrowerComponent machine)
    {
        if (ActiveSample(machine) is not { } sample)
            return;
        var research = ResearchFor(sample);
        var answer = Sequence(sample, machine.SelectedGene);
        var guess = machine.PendingSequence;
        var exact = Enumerable.Range(0, 6).Count(i => guess[i] == answer[i]);
        var total = "ATGC".Sum(c => Math.Min(answer.Count(a => a == c), guess.Count(a => a == c)));
        var result = Loc.GetString("genetics-sequence-result", ("guess", guess), ("exact", exact), ("misplaced", total - exact));
        var history = research.History.GetValueOrDefault(machine.SelectedGene, "");
        research.History[machine.SelectedGene] = history.Length > 1000 ? result : history + "\n" + result;
        if (exact == 6)
        {
            research.Decoded.Add(machine.SelectedGene);
            research.Evidence[machine.SelectedGene] = answer;
            machine.Discoveries.Add(machine.SelectedGene);
            research.History[machine.SelectedGene] = Loc.GetString("genetics-sequence-discovered");
            SyncDisk(machine);
        }
    }

    public bool TryInsertResearchDisk(EntityUid uid, EntityUid disk)
    {
        if (!TryComp<GeneticBodyGrowerComponent>(uid, out var machine)
            || !HasComp<GeneticResearchDiskComponent>(disk) || machine.Stage != GeneticGrowthStage.Idle
            || !Powered(uid) || machine.ResearchDisk.ContainedEntity != null || !_containers.Insert(disk, machine.ResearchDisk))
            return false;
        SyncDisk(machine);
        return true;
    }

    private void SyncDisk(GeneticBodyGrowerComponent machine)
    {
        if (machine.ResearchDisk.ContainedEntity is not { } uid || !TryComp<GeneticResearchDiskComponent>(uid, out var disk))
            return;
        machine.Discoveries.UnionWith(disk.Discoveries);
        disk.Discoveries.UnionWith(machine.Discoveries);
    }

    private void DiscoveryState(EntityUid uid, GeneticBodyGrowerComponent machine, GeneticBodyState state)
    {
        var sample = ActiveSample(machine);
        var research = sample?.Species != null ? ResearchFor(sample) : null;
        if (research != null && sample!.Analyzed && machine.ProductionSample == null)
            machine.Discoveries.UnionWith(research.Decoded);
        foreach (var gene in _prototypes.EnumeratePrototypes<GeneticModificationPrototype>().OrderBy(g => g.ID))
            state.Genes[gene.ID] = Loc.GetString(
                sample?.Analyzed == true && research!.Decoded.Contains(gene.ID) ? "genetics-gene-decoded" : "genetics-gene-pending",
                ("name", Loc.GetString(machine.Discoveries.Contains(gene.ID) ? gene.Name : gene.ResearchHint)));
        state.SelectedGene = machine.SelectedGene;
        state.GeneDescription = Loc.GetString("genetics-sequence-help");
        state.SequencePattern = sample?.Analyzed == true
            ? research!.Evidence.GetValueOrDefault(machine.SelectedGene, "??????") : "??????";
        state.GeneDescription += "\n" + Loc.GetString("genetics-study-progress",
            ("donor", sample?.Donor ?? "—"), ("known", state.SequencePattern.Count(c => c != '?')));
        state.SequenceFeedback = sample?.Analyzed == true
            ? research!.History.GetValueOrDefault(machine.SelectedGene, "") : "";
        state.CanCheckSequence = machine.Stage == GeneticGrowthStage.Idle && Powered(uid)
            && sample?.Analyzed == true && sample.TherapyCompatible
            && research!.Evidence.ContainsKey(machine.SelectedGene) && !research.Decoded.Contains(machine.SelectedGene);
        state.HasDisk = machine.ResearchDisk.ContainedEntity != null;
        state.CanSpectrum = machine.Stage == GeneticGrowthStage.Idle && Powered(uid)
            && sample?.Analyzed == true && sample.TherapyCompatible && !research!.Spectra.Contains(machine.SelectedGene);
        state.Composition = Loc.GetString("genetics-spectrum-unknown");
        if (sample?.Analyzed == true && research!.Spectra.Contains(machine.SelectedGene))
        {
            var code = Sequence(sample, machine.SelectedGene);
            state.Composition = Loc.GetString("genetics-spectrum-result", ("a", code.Count(c => c == 'A')),
                ("t", code.Count(c => c == 'T')), ("g", code.Count(c => c == 'G')), ("c", code.Count(c => c == 'C')));
        }
        state.CanEjectDisk = state.HasDisk && machine.Stage == GeneticGrowthStage.Idle;
    }
}

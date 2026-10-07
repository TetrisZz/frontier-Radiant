using System.Linq;
using System.Numerics;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared._radiant.Medical.Genetics;

namespace Content.Server._radiant.Medical.Genetics;

/// <summary>Uses the shared genetics controller for sample analysis, never for local production.</summary>
[RegisterComponent]
public sealed partial class GeneticResearchConsoleComponent : Component
{
    [DataField] public float LinkRange = 16;
}

public sealed partial class GeneticBodySystem
{
    private const string CapsuleSourcePort = "GeneticCapsuleSender";
    private const string CapsuleSinkPort = "GeneticCapsuleReceiver";
    [Dependency] private SharedDeviceLinkSystem _geneticLinks = default!;

    private void InitializeConsoleLinks()
    {
        SubscribeLocalEvent<GeneticResearchConsoleComponent, LinkAttemptEvent>(OnCapsuleLinkAttempt);
        SubscribeLocalEvent<GeneticResearchConsoleComponent, NewLinkEvent>(OnCapsuleLinked);
    }

    private void OnCapsuleLinkAttempt(EntityUid uid, GeneticResearchConsoleComponent console, LinkAttemptEvent args)
    {
        if (args.Source != uid || args.SourcePort != CapsuleSourcePort
            || args.SinkPort != CapsuleSinkPort
            || !HasComp<GeneticBodyGrowerComponent>(args.Sink)
            || HasComp<GeneticResearchConsoleComponent>(args.Sink)
            || !InLinkRange(uid, args.Sink, console.LinkRange))
        {
            args.Cancel();
            return;
        }

        if (TryComp<DeviceLinkSourceComponent>(uid, out var source)
            && source.Outputs.Values.SelectMany(targets => targets).Any(target => target != args.Sink))
        {
            args.Cancel();
            if (args.User is { } user)
                _popup.PopupEntity(Loc.GetString("genetics-link-disconnect-first"), uid, user);
        }
    }

    private void OnCapsuleLinked(EntityUid uid, GeneticResearchConsoleComponent console, NewLinkEvent args)
    {
        if (args.Source != uid || args.SourcePort != CapsuleSourcePort
            || !TryComp<GeneticBodyGrowerComponent>(args.Sink, out var pod))
            return;
        var machine = Comp<GeneticBodyGrowerComponent>(uid);
        machine.Discoveries.UnionWith(pod.Discoveries);
        SyncDisk(machine);
        PublishUi(uid);
    }

    private GeneticSampleComponent? ActiveSample(GeneticBodyGrowerComponent machine)
        => machine.ProductionSample ?? (machine.Sample.ContainedEntity is { } sample
            ? CompOrNull<GeneticSampleComponent>(sample) : null);

    private bool InLinkRange(EntityUid console, EntityUid capsule, float range)
    {
        if (TerminatingOrDeleted(console) || TerminatingOrDeleted(capsule))
            return false;
        var first = Transform(console);
        var second = Transform(capsule);
        return first.Anchored && second.Anchored && first.GridUid != null && first.GridUid == second.GridUid
            && Vector2.Distance(first.LocalPosition, second.LocalPosition) <= range;
    }

    public bool TryLinkConsole(EntityUid uid, EntityUid capsule)
    {
        if (!TryComp<GeneticResearchConsoleComponent>(uid, out var console)
            || !HasComp<GeneticBodyGrowerComponent>(capsule)
            || HasComp<GeneticResearchConsoleComponent>(capsule)
            || !InLinkRange(uid, capsule, console.LinkRange))
            return false;
        _geneticLinks.LinkDefaults(null, uid, capsule);
        return LinkedCapsule(uid, out var linked, out _) && linked == capsule;
    }

    private bool LinkedCapsule(EntityUid uid, out EntityUid capsule, out GeneticBodyGrowerComponent machine)
    {
        capsule = default;
        machine = default!;
        if (!TryComp<GeneticResearchConsoleComponent>(uid, out var console)
            || !TryComp<DeviceLinkSourceComponent>(uid, out var source))
            return false;
        var targets = source.Outputs.Values.SelectMany(output => output).Distinct().ToArray();
        if (targets.Length != 1)
            return false;
        var target = targets[0];
        if (!InLinkRange(uid, target, console.LinkRange)
            || !TryComp<GeneticBodyGrowerComponent>(target, out var pod)
            || HasComp<GeneticResearchConsoleComponent>(target))
            return false;
        capsule = target;
        machine = pod;
        return true;
    }

    private int OrderCost(GeneticBodyGrowerComponent pod, GeneticProduct product)
        => product == GeneticProduct.Body ? pod.BiomassCost
            : IsTherapy(product) ? pod.TherapyBiomassCost : pod.OrganBiomassCost;

    public bool TryOrder(EntityUid uid)
    {
        if (!HasComp<GeneticResearchConsoleComponent>(uid) || !Powered(uid)
            || !LinkedCapsule(uid, out var capsule, out var pod) || !Powered(capsule)
            || pod.Stage != GeneticGrowthStage.Idle || pod.Sample.ContainedEntity != null
            || pod.ProductionSample != null)
            return false;
        var research = Comp<GeneticBodyGrowerComponent>(uid);
        if (research.Stage != GeneticGrowthStage.Idle || ActiveSample(research) is not { } sample
            || !sample.Analyzed || !CanProduce(sample, research.Product, research)
            || !_materials.TryChangeMaterialAmount(capsule, "Biomass", -OrderCost(pod, research.Product)))
            return false;
        // Do not retain the mutable cartridge or move it out of the console.
        pod.ProductionSample = new GeneticSampleComponent
        {
            Species = sample.Species, Sex = sample.Sex, Donor = sample.Donor, Dna = sample.Dna,
            Analyzed = true, Researched = sample.Researched, TherapyCompatible = sample.TherapyCompatible,
            ProfileSnapshot = sample.ProfileSnapshot,
            BrainAppearance = sample.BrainAppearance == null ? null
                : _serialization.CreateCopy(sample.BrainAppearance, notNullableOverride: true),
        };
        pod.Product = research.Product;
        pod.OutputPrototype = OrganPrototype(sample, pod.Product);
        pod.Stage = GeneticGrowthStage.Growing;
        pod.Remaining = Duration(pod);
        pod.PowerFailure = 0;
        PublishUi(capsule);
        return true;
    }

    private void ConsoleState(EntityUid uid, GeneticBodyGrowerComponent machine, GeneticBodyState state)
    {
        var lines = new List<string>();
        foreach (var gene in _prototypes.EnumeratePrototypes<GeneticModificationPrototype>().OrderBy(g => g.ID))
        {
            if (!machine.Discoveries.Contains(gene.ID))
            {
                lines.Add(Loc.GetString(gene.ResearchHint) + ": " + Loc.GetString("genetics-archive-unknown"));
                continue;
            }
            var conflicts = string.Join(", ", gene.Conflicts.Select(id => Loc.GetString(_prototypes.Index<GeneticModificationPrototype>(id).Name)));
            lines.Add(Loc.GetString("genetics-archive-entry", ("name", Loc.GetString(gene.Name)),
                ("load", gene.Load), ("seconds", (int) gene.AdaptationSeconds),
                ("conflicts", conflicts.Length > 0 ? conflicts : Loc.GetString("genetics-archive-none"))));
            var product = gene.ID switch
            {
                "RadiantCoagulation" => GeneticProduct.Coagulation,
                "RadiantRegeneration" => GeneticProduct.Regeneration,
                "RadiantUltravision" => GeneticProduct.Ultravision,
                "RadiantStrength" => GeneticProduct.Strength,
                "RadiantSobriety" => GeneticProduct.Sobriety,
                "RadiantInsulation" => GeneticProduct.Insulation,
                _ => GeneticProduct.Hematopoiesis,
            };
            lines.Add(DescribeProduct(new GeneticBodyGrowerComponent { Product = product }));
        }
        state.Archive = Loc.GetString("genetics-archive-personal-note") + "\n\n" + string.Join("\n\n", lines);
        state.CanRelease = false; // Never teleport products to the operator.
        state.CanGrow = false;
        state.Connection = Loc.GetString("genetics-console-disconnected");
        state.ProductionStatus = "";
        state.Biomass = 0;
        if (!LinkedCapsule(uid, out var capsule, out var pod))
            return;
        if (machine.Sample.ContainedEntity == null && pod.Sample.ContainedEntity != null)
            state.NextStep = Loc.GetString("genetics-console-sample-in-pod");
        state.Connection = Loc.GetString("genetics-console-connected", ("name", Name(capsule)));
        state.ProductionStatus = Loc.GetString("genetics-console-production-status",
            ("stage", Loc.GetString("genetics-stage-" + pod.Stage.ToString().ToLowerInvariant())),
            ("seconds", (int) Math.Ceiling(pod.Remaining)));
        state.Cost = OrderCost(pod, machine.Product);
        state.Biomass = _materials.GetMaterialAmount(capsule, "Biomass");
        if (!Powered(capsule))
            state.Connection += "\n" + Loc.GetString("genetics-ui-unpowered");
        state.CanGrow = Powered(uid) && Powered(capsule) && machine.Stage == GeneticGrowthStage.Idle
            && pod.Stage == GeneticGrowthStage.Idle && pod.Sample.ContainedEntity == null
            && ActiveSample(machine) is { Analyzed: true } sample
            && CanProduce(sample, machine.Product, machine) && state.Biomass >= state.Cost;
        if (pod.Sample.ContainedEntity != null)
            state.Connection += "\n" + Loc.GetString("genetics-console-remove-pod-sample");
    }
}

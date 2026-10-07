using System.Linq;
using Content.Shared.Body.Components;
using Content.Shared.Body.Prototypes;
using Content.Shared.Hands.EntitySystems;
using Content.Shared._radiant.Medical.Genetics;
using Content.Shared._radiant.Skills;
using Robust.Server.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Medical.Genetics;

public sealed partial class GeneticBodySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedHandsSystem _hands = default!;

    private void InitializeUi()
    {
        SubscribeLocalEvent<GeneticBodyGrowerComponent, BoundUIOpenedEvent>(OnUiOpened);
        SubscribeLocalEvent<GeneticBodyGrowerComponent, GeneticBodyMessage>(OnUiMessage);
    }

    private void OnUiOpened(Entity<GeneticBodyGrowerComponent> ent, ref BoundUIOpenedEvent args)
        => PublishUi(ent);

    // Use the species' native anatomy, never an organ ID supplied by the client.
    public string? OrganPrototype(GeneticSampleComponent sample, GeneticProduct product)
    {
        if (sample.Species == null || product == GeneticProduct.Body
            || !_prototypes.TryIndex<Content.Shared.Humanoid.Prototypes.SpeciesPrototype>(sample.Species, out var species))
            return null;
        var mob = _prototypes.Index(species.Prototype);
        if (!mob.Components.TryGetValue("Body", out var entry) || entry.Component is not BodyComponent body
            || body.Prototype is not { } id || !_prototypes.TryIndex(id, out var template))
            return null;
        var slotName = product switch
        {
            GeneticProduct.Heart => "heart", GeneticProduct.Lungs => "lungs",
            GeneticProduct.Liver => "liver", GeneticProduct.Kidneys => "kidneys",
            GeneticProduct.Stomach => "stomach", GeneticProduct.Eyes => "eyes",
            GeneticProduct.Tongue => "tongue", _ => null,
        };
        if (slotName == null)
            return null;
        foreach (var slot in template.Slots.Values)
        {
            if (!slot.Organs.TryGetValue(slotName, out var organ)
                || !_prototypes.TryIndex<EntityPrototype>(organ, out var prototype)
                || !prototype.Components.ContainsKey("Organ")
                || prototype.Components.ContainsKey("Brain"))
                continue;
            return organ;
        }
        return null;
    }

    public bool SelectProduct(EntityUid uid, GeneticProduct product)
    {
        if (!TryComp<GeneticBodyGrowerComponent>(uid, out var machine)
            || machine.Stage != GeneticGrowthStage.Idle
            || machine.Sample.ContainedEntity is not { } sample
            || !TryComp<GeneticSampleComponent>(sample, out var data) || !data.Analyzed
            || !CanProduce(data, product, machine))
            return false;
        machine.Product = product;
        return true;
    }

    private int Cost(GeneticBodyGrowerComponent machine)
        => machine.Product == GeneticProduct.Body ? machine.BiomassCost
            : IsTherapy(machine.Product) ? machine.TherapyBiomassCost : machine.OrganBiomassCost;

    private float Duration(GeneticBodyGrowerComponent machine)
        => machine.Product == GeneticProduct.Body ? machine.GrowthSeconds
            : IsTherapy(machine.Product) ? machine.TherapySeconds : machine.OrganGrowthSeconds;

    public GeneticBodyState BuildUiState(EntityUid uid)
    {
        var machine = Comp<GeneticBodyGrowerComponent>(uid);
        var sample = ActiveSample(machine);
        var idle = machine.Stage == GeneticGrowthStage.Idle;
        var powered = Powered(uid);
        var biomass = HasComp<GeneticResearchConsoleComponent>(uid) ? 0 : _materials.GetMaterialAmount(uid, "Biomass");
        var duration = machine.Stage == GeneticGrowthStage.Analyzing ? machine.AnalysisSeconds
            : machine.Stage == GeneticGrowthStage.Researching ? machine.ResearchSeconds
            : machine.Stage == GeneticGrowthStage.Sequencing ? 10 : Duration(machine);
        if (machine.Stage == GeneticGrowthStage.Spectrometry) duration = 30;
        var state = new GeneticBodyState
        {
            Sample = sample == null ? Loc.GetString("genetics-ui-no-sample") : SampleDescription(sample),
            SampleEntity = machine.Sample.ContainedEntity is { } cartridge ? GetNetEntity(cartridge) : null,
            NextStep = NextStep(machine, sample, powered),
            ProductDescription = DescribeProduct(machine),
            Profile = sample?.Analyzed == true
                ? Loc.GetString("genetics-profile-snapshot") + "\n"
                    + (sample.ProfileSnapshot.Length > 0 ? sample.ProfileSnapshot : Loc.GetString("genetics-profile-unavailable"))
                    + "\n" + Loc.GetString(sample.Researched ? "genetics-research-complete" : "genetics-research-pending")
                : Loc.GetString("genetics-profile-analyze-first"),
            CanResearch = idle && powered && sample?.Analyzed == true && sample.TherapyCompatible && HasNewEvidence(machine, sample),
            Status = Loc.GetString("genetics-stage-" + machine.Stage.ToString().ToLowerInvariant()),
            Warning = !powered
                ? idle ? Loc.GetString("genetics-ui-unpowered")
                    : Loc.GetString("genetics-no-power", ("seconds", (int) Math.Max(0, machine.PowerFailureSeconds - machine.PowerFailure)))
                : sample?.Analyzed == true && idle && biomass < Cost(machine) ? Loc.GetString("genetics-ui-low-biomass") : "",
            Biomass = biomass, Cost = Cost(machine), Seconds = (int) Math.Ceiling(machine.Remaining),
            Progress = idle ? 0 : machine.Stage == GeneticGrowthStage.Ready ? 1
                : Math.Clamp(1 - machine.Remaining / Math.Max(1, duration), 0, 1),
            Selected = machine.Product,
            CanAnalyze = powered && idle && sample?.Species != null && !sample.Analyzed,
            CanGrow = powered && idle && sample?.Analyzed == true && biomass >= Cost(machine),
            CanRelease = powered && machine.Stage == GeneticGrowthStage.Ready,
            CanEject = idle && sample != null,
            CanSelect = idle && sample?.Analyzed == true,
        };
        state.Products.Add(GeneticProduct.Body);
        if (sample?.Analyzed == true)
            foreach (var product in Enum.GetValues<GeneticProduct>())
                if (product != GeneticProduct.Body && CanProduce(sample, product, machine))
                    state.Products.Add(product);
        DiscoveryState(uid, machine, state);
        if (machine.ProductionSample != null && !state.Products.Contains(machine.Product))
            state.Products.Add(machine.Product);
        if (HasComp<GeneticResearchConsoleComponent>(uid))
        {
            // Research consoles have no biomass storage of their own.
            if (powered) state.Warning = "";
            ConsoleState(uid, machine, state);
        }
        return state;
    }

    private void PublishUi(EntityUid uid)
        => _ui.SetUiState(uid, GeneticBodyUiKey.Key, BuildUiState(uid));

    private string NextStep(GeneticBodyGrowerComponent machine, GeneticSampleComponent? sample, bool powered)
    {
        if (!powered)
            return Loc.GetString("genetics-help-power");
        if (sample == null)
            return Loc.GetString("genetics-help-sample");
        if (machine.Stage == GeneticGrowthStage.Ready)
            return Loc.GetString(machine.Product == GeneticProduct.Body ? "genetics-help-release-body" : "genetics-help-release-item");
        if (machine.Stage != GeneticGrowthStage.Idle)
            return Loc.GetString("genetics-help-wait");
        if (!sample.Analyzed)
            return Loc.GetString("genetics-help-analyze", ("seconds", (int) machine.AnalysisSeconds));
        if (ResearchFor(sample).Decoded.Contains(machine.SelectedGene))
            return Loc.GetString("genetics-help-produce");
        if (sample.TherapyCompatible && HasNewEvidence(machine, sample))
            return Loc.GetString("genetics-help-research", ("seconds", (int) machine.ResearchSeconds));
        if (sample.TherapyCompatible && ResearchFor(sample).Decoded.Count < _prototypes.EnumeratePrototypes<GeneticModificationPrototype>().Count())
            return Loc.GetString("genetics-help-decode");
        return Loc.GetString("genetics-help-produce");
    }


    private string DescribeProduct(GeneticBodyGrowerComponent machine)
    {
        var key = machine.Product switch
        {
            GeneticProduct.Body => "genetics-help-body",
            GeneticProduct.Hematopoiesis => "genetics-help-activator",
            GeneticProduct.HematopoiesisRemoval => "genetics-help-suppressor",
            GeneticProduct.Coagulation => "genetics-help-coagulation",
            GeneticProduct.Regeneration => "genetics-help-regeneration",
            GeneticProduct.Ultravision => "genetics-help-ultravision",
            GeneticProduct.Strength => "genetics-help-strength",
            GeneticProduct.Sobriety => "genetics-help-sobriety",
            GeneticProduct.Insulation => "genetics-help-insulation",
            GeneticProduct.CoagulationRemoval or GeneticProduct.RegenerationRemoval
                or GeneticProduct.UltravisionRemoval or GeneticProduct.StrengthRemoval
                or GeneticProduct.SobrietyRemoval or GeneticProduct.InsulationRemoval => "genetics-help-suppressor-generic",
            _ => "genetics-help-organ",
        };
        var gene = _prototypes.Index<GeneticModificationPrototype>(GeneFor(machine.Product) ?? "RadiantHematopoiesis");
        return Loc.GetString(key, ("blood", gene.BloodPerSecond * 60),
                ("nutrition", gene.NutritionPerBlood), ("load", gene.Load), ("adaptation", (int) gene.AdaptationSeconds))
            + "\n" + Loc.GetString("genetics-help-recipe",
                ("seconds", (int) Duration(machine)), ("cost", Cost(machine)));
    }

    private void OnUiMessage(Entity<GeneticBodyGrowerComponent> ent, ref GeneticBodyMessage args)
    {
        if (!CanOperate(ent, args.Actor))
            return;
        var isConsole = HasComp<GeneticResearchConsoleComponent>(ent);
        if (!isConsole && args.Command is GeneticCommand.Research or GeneticCommand.SelectGene or GeneticCommand.CheckSequence or GeneticCommand.Spectrum)
        {
            _popup.PopupEntity(Loc.GetString("genetics-console-required"), ent, args.Actor);
            return;
        }
        if (args.Command is GeneticCommand.Research or GeneticCommand.SelectGene or GeneticCommand.CheckSequence or GeneticCommand.Spectrum)
        {
            if (_skills.Level(args.Actor, ProfessionalSkill.Science) < 2
                && _skills.Level(args.Actor, ProfessionalSkill.Medicine) < 4)
            {
                _popup.PopupEntity(Loc.GetString("genetics-research-skill"), ent, args.Actor);
                return;
            }
        }
        else if (args.Command is not (GeneticCommand.Eject or GeneticCommand.EjectDisk)
            && !_skills.Check(args.Actor, ProfessionalSkill.Medicine, 3, serverPopup: true))
            return;
        var state = BuildUiState(ent);
        var success = false;
        switch (args.Command)
        {
            case GeneticCommand.Spectrum:
                success = TrySpectrum(ent);
                break;
            case GeneticCommand.SelectGene:
                if (ent.Comp.Stage == GeneticGrowthStage.Idle && state.Genes.ContainsKey(args.Text))
                {
                    ent.Comp.SelectedGene = args.Text;
                    success = true;
                }
                break;
            case GeneticCommand.CheckSequence:
                success = TrySequence(ent, args.Text);
                break;
            case GeneticCommand.EjectDisk:
                if (ent.Comp.Stage == GeneticGrowthStage.Idle && ent.Comp.ResearchDisk.ContainedEntity is { } disk)
                {
                    SyncDisk(ent.Comp);
                    success = _containers.Remove(disk, ent.Comp.ResearchDisk);
                    if (success) _hands.TryPickupAnyHand(args.Actor, disk);
                }
                break;
            case GeneticCommand.Research:
                success = TryResearch(ent);
                break;
            case GeneticCommand.Select:
                success = SelectProduct(ent, args.Product);
                break;
            case GeneticCommand.Analyze:
                success = state.CanAnalyze && TryStart(ent);
                break;
            case GeneticCommand.Grow:
                success = state.CanGrow && args.Product == ent.Comp.Product && (isConsole ? TryOrder(ent) : TryStart(ent));
                break;
            case GeneticCommand.Release:
                if (isConsole) break;
                var isOrgan = ent.Comp.Product != GeneticProduct.Body;
                if (TryRelease(ent) is { } result)
                {
                    if (isOrgan)
                        _hands.TryPickupAnyHand(args.Actor, result);
                    success = true;
                }
                break;
            case GeneticCommand.Eject:
                if (state.CanEject && ent.Comp.Sample.ContainedEntity is { } sample)
                {
                    success = _containers.Remove(sample, ent.Comp.Sample);
                    if (success) _hands.TryPickupAnyHand(args.Actor, sample);
                }
                break;
        }
        if (!success)
            _popup.PopupEntity(Loc.GetString("genetics-ui-action-failed"), ent, args.Actor);
        PublishUi(ent);
    }
}

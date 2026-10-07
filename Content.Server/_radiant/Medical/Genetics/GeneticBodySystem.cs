using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Power.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Examine;
using Content.Shared.Forensics.Components;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared._radiant.Skills;
using Content.Shared._radiant.Medical.Genetics;
using Content.Server._radiant.Medical.Surgery;
using Content.Shared.Forensics;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Content.Shared.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._radiant.Medical.Genetics;

/// <summary>A sealed snapshot, not a live link to the donor or their mind.</summary>
[RegisterComponent]
public sealed partial class GeneticSampleComponent : Component
{
    [DataField] public string? Species;
    [DataField] public Sex Sex;
    [DataField] public string Donor = "";
    [DataField] public string Dna = "";
    [DataField] public bool Analyzed;
    [DataField] public bool Researched;
    [DataField] public bool TherapyCompatible;
    [DataField] public string ProfileSnapshot = "";
    // Brain cartridges retain the donor's original appearance, not just their species.
    [DataField] public HumanoidAppearanceComponent? BrainAppearance;
}

public enum GeneticGrowthStage : byte { Idle, Analyzing, Growing, Ready, Researching, Sequencing, Spectrometry }

[RegisterComponent]
public sealed partial class GeneticBodyGrowerComponent : Component
{
    [DataField] public float AnalysisSeconds = 30;
    [DataField] public float GrowthSeconds = 600;
    [DataField] public int BiomassCost = 100;
    [DataField] public float PowerFailureSeconds = 180;
    [DataField] public float OrganGrowthSeconds = 180;
    [DataField] public int OrganBiomassCost = 20;
    [DataField] public float ResearchSeconds = 90;
    [DataField] public float TherapySeconds = 60;
    [DataField] public int TherapyBiomassCost = 30;
    [ViewVariables] public GeneticProduct Product;
    [ViewVariables] public string? OutputPrototype;
    public float UiRefresh;
    [ViewVariables] public GeneticGrowthStage Stage;
    [ViewVariables] public float Remaining;
    [ViewVariables] public float PowerFailure;
    public ContainerSlot Sample = default!;
    // Immutable donor snapshot for an order submitted by a research console.
    public GeneticSampleComponent? ProductionSample;
    public ContainerSlot ResearchDisk = default!;
    public HashSet<string> Discoveries = new();
    public string SelectedGene = "RadiantHematopoiesis";
    public string PendingSequence = "";
    [DataField] public SoundSpecifier StartSound = new SoundPathSpecifier("/Audio/Machines/button.ogg", AudioParams.Default.WithVolume(-8));
    [DataField] public SoundSpecifier FinishSound = new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg", AudioParams.Default.WithVolume(-6));
    [DataField] public SoundSpecifier ReleaseSound = new SoundPathSpecifier("/Audio/Items/hiss.ogg", AudioParams.Default.WithVolume(-6));
    public GeneticGrowthStage SoundStage;
}

/// <summary>
/// First genetics workflow: collect, analyze, grow, release. Never transfers a mind
/// or copies the donor's original identity record into the new body's organs.
/// </summary>
public sealed partial class GeneticBodySystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedHumanoidAppearanceSystem _appearance = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedMaterialStorageSystem _materials = default!;
    [Dependency] private SharedProfessionalSkillsSystem _skills = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedAppearanceSystem _visuals = default!;
    [Dependency] private SharedAmbientSoundSystem _geneticAmbience = default!;
    [Dependency] private SharedAudioSystem _geneticAudio = default!;
    [Dependency] private ISerializationManager _serialization = default!;

    public override void Initialize()
    {
        InitializeUi();
        InitializeDiscovery();
        InitializeConsoleLinks();
        SubscribeLocalEvent<GeneticSampleComponent, AfterInteractEvent>(OnSample);
        SubscribeLocalEvent<GeneticSampleComponent, ExaminedEvent>(OnSampleExamine);
        SubscribeLocalEvent<GeneticBodyGrowerComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<GeneticBodyGrowerComponent, InteractUsingEvent>(OnInsert);
        SubscribeLocalEvent<GeneticBodyGrowerComponent, GetVerbsEvent<ActivationVerb>>(OnVerbs);
        SubscribeLocalEvent<GeneticBodyGrowerComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<GeneticBodyGrowerComponent, EntRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnInit(Entity<GeneticBodyGrowerComponent> ent, ref ComponentInit args)
    {
        ent.Comp.Sample = _containers.EnsureContainer<ContainerSlot>(ent, "genetic-sample");
        ent.Comp.ResearchDisk = _containers.EnsureContainer<ContainerSlot>(ent, "genetic-research-disk");
    }

    private IEnumerable<EntityUid> Brains(EntityUid uid)
        => _body.GetBodyChildren(uid).SelectMany(p => _body.GetPartOrgans(p.Id))
            .Where(o => HasComp<BrainComponent>(o.Id)).Select(o => o.Id);

    public bool Collect(EntityUid sample, EntityUid donor)
    {
        if (HasComp<BrainComponent>(donor))
            return CollectFromBrain(sample, donor);

        if (!TryComp<GeneticSampleComponent>(sample, out var data) || data.Species != null
            || !TryComp<HumanoidAppearanceComponent>(donor, out var look)
            || look.Sex is not (Sex.Male or Sex.Female)
            || !HasComp<BloodstreamComponent>(donor) || HasComp<RottingComponent>(donor)
            || !TryComp<DnaComponent>(donor, out var dna) || string.IsNullOrEmpty(dna.DNA)
            || !_prototypes.TryIndex(look.Species, out var species) || !species.RoundStart
            || !Brains(donor).Any())
            return false;

        data.Species = species.ID;
        data.Sex = look.Sex;
        data.Donor = Name(donor);
        data.Dna = dna.DNA;
        _visuals.SetData(sample, GeneticSampleVisuals.Filled, true);
        data.TherapyCompatible = HasComp<Content.Shared.Nutrition.Components.HungerComponent>(donor);
        data.ProfileSnapshot = EntityManager.System<GeneticModificationSystem>().ProfileText(donor);
        _metadata.SetEntityName(sample, Loc.GetString("genetics-sample-labelled", ("donor", data.Donor)));
        return true;
    }

    private bool CollectFromBrain(EntityUid sample, EntityUid brain)
    {
        if (!TryComp<GeneticSampleComponent>(sample, out var data) || data.Species != null
            || !TryComp<BrainIdentityMemoryComponent>(brain, out var memory)
            || string.IsNullOrWhiteSpace(memory.Dna)
            || memory.Appearance.Sex is not (Sex.Male or Sex.Female)
            || !_prototypes.TryIndex(memory.Appearance.Species, out SpeciesPrototype? species)
            || !species.RoundStart)
            return false;

        data.Species = species.ID;
        data.Sex = memory.Appearance.Sex;
        data.Donor = memory.OriginalName;
        data.Dna = memory.Dna;
        data.BrainAppearance = _serialization.CreateCopy(memory.Appearance, notNullableOverride: true);
        _visuals.SetData(sample, GeneticSampleVisuals.Filled, true);
        _metadata.SetEntityName(sample, Loc.GetString("genetics-sample-labelled", ("donor", data.Donor)));
        return true;
    }

    private void OnSample(Entity<GeneticSampleComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;
        args.Handled = true;
        if (!_skills.Check(args.User, ProfessionalSkill.Medicine, 2, serverPopup: true))
            return;
        var key = Collect(ent, target) ? "genetics-collected" : "genetics-invalid-sample";
        _popup.PopupEntity(Loc.GetString(key), target, args.User);
    }

    private string SampleDescription(GeneticSampleComponent data)
        => data.Species == null ? Loc.GetString("genetics-sample-empty")
            : Loc.GetString("genetics-sample-details", ("donor", data.Donor),
                ("species", Loc.GetString(_prototypes.Index<SpeciesPrototype>(data.Species).Name)),
                ("sex", Loc.GetString("restoration-sex-" + data.Sex.ToString().ToLowerInvariant())),
                ("status", Loc.GetString(data.Analyzed ? "genetics-analyzed" : "genetics-unexamined")));

    private void OnSampleExamine(Entity<GeneticSampleComponent> ent, ref ExaminedEvent args)
        => args.PushText(SampleDescription(ent.Comp));

    private void OnInsert(Entity<GeneticBodyGrowerComponent> ent, ref InteractUsingEvent args)
    {
        if (!args.Handled && HasComp<GeneticResearchDiskComponent>(args.Used))
        {
            args.Handled = true;
            if (!HasComp<GeneticResearchConsoleComponent>(ent))
            {
                _popup.PopupEntity(Loc.GetString("genetics-console-required"), ent, args.User);
                return;
            }
            if ((_skills.Level(args.User, ProfessionalSkill.Science) < 2 && _skills.Level(args.User, ProfessionalSkill.Medicine) < 4)
                || !TryInsertResearchDisk(ent, args.Used))
                _popup.PopupEntity(Loc.GetString("genetics-disk-insert-failed"), ent, args.User);
            PublishUi(ent);
            return;
        }
        if (args.Handled || !TryComp<GeneticSampleComponent>(args.Used, out var sample))
            return;
        args.Handled = true;
        if (sample.Species == null || ent.Comp.Stage != GeneticGrowthStage.Idle
            || ent.Comp.Sample.ContainedEntity != null || !_containers.Insert(args.Used, ent.Comp.Sample))
            _popup.PopupEntity(Loc.GetString("genetics-insert-failed"), ent, args.User);
    }

    private bool Powered(EntityUid uid)
        => Transform(uid).Anchored && TryComp<ApcPowerReceiverComponent>(uid, out var power) && power.Powered;

    private bool CanOperate(EntityUid uid, EntityUid user)
        => !TerminatingOrDeleted(uid) && !TerminatingOrDeleted(user)
            && _interaction.InRangeUnobstructed(user, uid) && _blocker.CanInteract(user, uid);

    private void OnVerbs(Entity<GeneticBodyGrowerComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (HasComp<GeneticResearchConsoleComponent>(ent))
            return;
        if (!args.CanAccess || !args.CanInteract)
            return;
        var user = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("genetics-start"),
            Act = () =>
            {
                if (!CanOperate(ent, user) || !_skills.Check(user, ProfessionalSkill.Medicine, 3, serverPopup: true))
                    return;
                _popup.PopupEntity(Loc.GetString(TryStart(ent) ? "genetics-started" : "genetics-start-failed"), ent, user);
            },
        });
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("genetics-release"),
            Act = () =>
            {
                if (!CanOperate(ent, user) || !_skills.Check(user, ProfessionalSkill.Medicine, 3, serverPopup: true))
                    return;
                _popup.PopupEntity(Loc.GetString(TryRelease(ent) != null ? "genetics-released" : "genetics-not-ready"), ent, user);
            },
        });
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("genetics-eject"),
            Act = () =>
            {
                if (!CanOperate(ent, user))
                    return;
                if (ent.Comp.Stage != GeneticGrowthStage.Idle)
                {
                    _popup.PopupEntity(Loc.GetString("genetics-busy"), ent, user);
                    return;
                }
                if (ent.Comp.Sample.ContainedEntity is { } sample)
                    _containers.Remove(sample, ent.Comp.Sample);
            },
        });
    }

    public bool TryStart(EntityUid uid)
    {
        if (!TryComp<GeneticBodyGrowerComponent>(uid, out var machine)
            || machine.Stage != GeneticGrowthStage.Idle || !Powered(uid)
            || machine.Sample.ContainedEntity is not { } sample
            || !TryComp<GeneticSampleComponent>(sample, out var data) || data.Species == null)
            return false;

        // Analysis does not charge resources. Starting growth is a separate action.
        if (!data.Analyzed)
        {
            machine.Stage = GeneticGrowthStage.Analyzing;
            machine.Remaining = machine.AnalysisSeconds;
        }
        else
        {
            if (HasComp<GeneticResearchConsoleComponent>(uid))
                return false;
            var organ = OrganPrototype(data, machine.Product);
            if (!CanProduce(data, machine.Product, machine))
                return false;
            if (!_materials.TryChangeMaterialAmount(uid, "Biomass", -Cost(machine)))
                return false;
            machine.OutputPrototype = organ;
            machine.Stage = GeneticGrowthStage.Growing;
            machine.Remaining = Duration(machine);
        }
        machine.PowerFailure = 0;
        return true;
    }

    private void OnRemoved(Entity<GeneticBodyGrowerComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == "genetic-sample")
        {
            Reset(ent.Comp);
            ent.Comp.Product = GeneticProduct.Body;
        }
    }

    private static void Reset(GeneticBodyGrowerComponent machine)
    {
        machine.Stage = GeneticGrowthStage.Idle;
        machine.Remaining = 0;
        machine.PowerFailure = 0;
        machine.OutputPrototype = null;
        machine.PendingSequence = "";
        machine.ProductionSample = null;
    }

    private void OnExamine(Entity<GeneticBodyGrowerComponent> ent, ref ExaminedEvent args)
    {
        args.PushText(Loc.GetString("genetics-machine-status",
            ("stage", Loc.GetString("genetics-stage-" + ent.Comp.Stage.ToString().ToLowerInvariant())),
            ("seconds", (int) Math.Ceiling(ent.Comp.Remaining)), ("cost", Cost(ent.Comp))));
        if (ent.Comp.Sample.ContainedEntity is { } sample && TryComp<GeneticSampleComponent>(sample, out var data))
            args.PushText(SampleDescription(data));
        if (!Powered(ent))
            args.PushText(Loc.GetString("genetics-no-power", ("seconds",
                (int) Math.Max(0, ent.Comp.PowerFailureSeconds - ent.Comp.PowerFailure))));
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<GeneticBodyGrowerComponent>();
        while (query.MoveNext(out var uid, out var machine))
        {
            if (MetaData(uid).EntityPaused)
                _geneticAmbience.SetAmbience(uid, false);
            if (!MetaData(uid).EntityPaused)
            {
                Advance(uid, frameTime);
                _visuals.SetData(uid, GeneticBodyVisuals.State,
                    Powered(uid) ? machine.Stage.ToString() : "Off");
                machine.UiRefresh -= frameTime;
                if (machine.UiRefresh <= 0)
                {
                    machine.UiRefresh = 1;
                    PublishUi(uid);
                }
            }
        }
    }

    public void Advance(EntityUid uid, float seconds)
    {
        AdvanceCycle(uid, seconds);
        UpdateMachineSound(uid);
    }

    private void UpdateMachineSound(EntityUid uid)
    {
        var machine = Comp<GeneticBodyGrowerComponent>(uid);
        var working = machine.Stage is not (GeneticGrowthStage.Idle or GeneticGrowthStage.Ready)
            && Powered(uid) && !MetaData(uid).EntityPaused;
        _geneticAmbience.SetAmbience(uid, working);
        if (working && machine.SoundStage is GeneticGrowthStage.Idle or GeneticGrowthStage.Ready)
            _geneticAudio.PlayPvs(machine.StartSound, uid);
        machine.SoundStage = machine.Stage;
    }

    private void AdvanceCycle(EntityUid uid, float seconds)
    {
        var machine = Comp<GeneticBodyGrowerComponent>(uid);
        if (seconds <= 0 || machine.Stage == GeneticGrowthStage.Idle)
            return;
        if (ActiveSample(machine) is not { } data)
        {
            Reset(machine);
            return;
        }
        if (!Powered(uid))
        {
            machine.PowerFailure += seconds;
            if (machine.PowerFailure >= machine.PowerFailureSeconds)
            {
                Reset(machine);
                _popup.PopupEntity(Loc.GetString("genetics-cycle-lost"), uid);
            }
            return;
        }
        machine.PowerFailure = 0;
        if (machine.Stage == GeneticGrowthStage.Ready)
            return;
        machine.Remaining = Math.Max(0, machine.Remaining - seconds);
        if (machine.Remaining > 0)
            return;
        _geneticAudio.PlayPvs(machine.FinishSound, uid);
        if (machine.Stage == GeneticGrowthStage.Spectrometry)
        {
            ResearchFor(data).Spectra.Add(machine.SelectedGene);
            ResearchFor(data).Evidence.TryAdd(machine.SelectedGene, "??????");
            Reset(machine);
        }
        else if (machine.Stage == GeneticGrowthStage.Researching)
        {
            data.Researched = true;
            StudySample(machine, data);
            Reset(machine);
            _popup.PopupEntity(Loc.GetString("genetics-research-done"), uid);
        }
        else if (machine.Stage == GeneticGrowthStage.Sequencing)
        {
            FinishSequence(machine);
            Reset(machine);
        }
        else if (machine.Stage == GeneticGrowthStage.Analyzing)
        {
            data.Analyzed = true;
            Reset(machine);
            _popup.PopupEntity(Loc.GetString("genetics-analysis-done"), uid);
        }
        else
        {
            machine.Stage = GeneticGrowthStage.Ready;
            _popup.PopupEntity(Loc.GetString("genetics-growth-done"), uid);
        }
    }

    public EntityUid? TryRelease(EntityUid uid)
    {
        var result = ReleaseCulture(uid);
        if (result != null)
        {
            _geneticAudio.PlayPvs(Comp<GeneticBodyGrowerComponent>(uid).ReleaseSound, uid);
            UpdateMachineSound(uid);
        }
        return result;
    }

    private EntityUid? ReleaseCulture(EntityUid uid)
    {
        if (!TryComp<GeneticBodyGrowerComponent>(uid, out var machine)
            || machine.Stage != GeneticGrowthStage.Ready || !Powered(uid)
            || HasComp<GeneticResearchConsoleComponent>(uid)
            || ActiveSample(machine) is not { } data || data.Species == null)
            return null;

        if (IsTherapy(machine.Product))
            return ReleaseTherapy(uid, machine, data);
        if (machine.Product != GeneticProduct.Body)
        {
            if (machine.OutputPrototype is not { } organ
                || OrganPrototype(data, machine.Product) != organ)
                return null;
            var output = Spawn(organ, Transform(uid).Coordinates);
            Reset(machine);
            return output;
        }

        var species = _prototypes.Index<SpeciesPrototype>(data.Species);
        // Materialize only on release: a ready culture is sustained by the powered pod,
        // not a living mob taking suffocation damage inside an inaccessible container.
        var grown = Spawn(species.Prototype, Transform(uid).Coordinates);
        var brains = Brains(grown).ToArray();
        if (brains.Length == 0)
        {
            QueueDel(grown);
            return null;
        }
        foreach (var brain in brains)
        {
            if (!_body.RemoveOrgan(brain))
            {
                QueueDel(grown);
                return null;
            }
            QueueDel(brain);
        }
        _appearance.SetSex(grown, data.Sex);
        _appearance.SetGender((grown, Comp<HumanoidAppearanceComponent>(grown)),
            data.Sex == Sex.Female ? Robust.Shared.Enums.Gender.Female : Robust.Shared.Enums.Gender.Male);
        _metadata.SetEntityName(grown, Loc.GetString("genetics-grown-body"));
        if (data.BrainAppearance is { } saved)
        {
            var appearance = Comp<HumanoidAppearanceComponent>(grown);
            _appearance.ApplyAppearanceSnapshot(grown, saved, appearance);
            _appearance.SetHeight(grown, saved.Height, humanoid: appearance);
            _appearance.SetWidth(grown, saved.Width, humanoid: appearance);
            appearance.HairColoringMode = saved.HairColoringMode;
            appearance.HairGradientColor = saved.HairGradientColor;
            appearance.HairGradientDirection = saved.HairGradientDirection;
            appearance.FacialHairColoringMode = saved.FacialHairColoringMode;
            appearance.FacialHairGradientColor = saved.FacialHairGradientColor;
            appearance.FacialHairGradientDirection = saved.FacialHairGradientDirection;
            appearance.Voice = saved.Voice;
            Dirty(grown, appearance);
        }
        if (data.BrainAppearance != null && !string.IsNullOrWhiteSpace(data.Dna))
        {
            var dna = EnsureComp<DnaComponent>(grown);
            dna.DNA = data.Dna;
            Dirty(grown, dna);
            var changed = new GenerateDnaEvent { Owner = grown, DNA = data.Dna };
            RaiseLocalEvent(grown, ref changed);
        }
        Reset(machine);
        return grown;
    }
}

using System.Linq;
using Content.Server.Power.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Station;
using Content.Shared._radiant.Medical.Virology;
using Content.Shared._radiant.Skills;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Server._radiant.Medical.Virology;

public sealed partial class VirologyLabSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private VirologySystem _diseases = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedProfessionalSkillsSystem _skills = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private SharedMaterialStorageSystem _materials = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private PaperSystem _paper = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private SharedAppearanceSystem _visuals = default!;
    [Dependency] private SharedStationSystem _stations = default!;
    [Dependency] private Content.Shared.Audio.SharedAmbientSoundSystem _ambience = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<VirologySampleComponent, AfterInteractEvent>(OnCollect);
        SubscribeLocalEvent<VirologySampleComponent, VirologyUseDoAfterEvent>(OnCollected);
        SubscribeLocalEvent<VirologySampleComponent, ExaminedEvent>(OnSampleExamined);
        SubscribeLocalEvent<VirologyDoseComponent, AfterInteractEvent>(OnInject);
        SubscribeLocalEvent<VirologyDoseComponent, VirologyUseDoAfterEvent>(OnInjected);
        SubscribeLocalEvent<VirologyDoseComponent, ExaminedEvent>(OnDoseExamined);
        SubscribeLocalEvent<VirologyMachineComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<VirologyMachineComponent, InteractUsingEvent>(OnInsert);
        SubscribeLocalEvent<VirologyMachineComponent, VirologyMessage>(OnMessage);
        SubscribeLocalEvent<VirologyMachineComponent, BoundUIOpenedEvent>(OnOpened);
    }

    public bool Collect(EntityUid sample, EntityUid target)
    {
        if (!TryComp<VirologySampleComponent>(sample, out var data) || data.Collected)
            return false;
        data.Diseases.Clear();
        if (HasComp<BloodstreamComponent>(target))
        {
            if (TryComp<VirologyCarrierComponent>(target, out var carrier))
                data.Diseases.AddRange(carrier.Infections.Keys);
        }
        else if (TryComp<VirologyOrganComponent>(target, out var organ))
            data.Diseases.AddRange(organ.Diseases);
        else if (_solutions.TryGetDrawableSolution(target, out _, out var liquid))
        {
            foreach (var proto in _prototypes.EnumeratePrototypes<RadiantDiseasePrototype>())
                if (liquid.GetTotalPrototypeQuantity(proto.Reagent) > 0) data.Diseases.Add(proto.ID);
        }
        else return false;
        data.Collected = true;
        data.Donor = Name(target);
        _metadata.SetEntityName(sample, Loc.GetString("virology-sample-name", ("donor", data.Donor)));
        return true;
    }

    private void OnCollect(Entity<VirologySampleComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target) return;
        // Preserve the existing swab's botany behavior on non-medical targets.
        if (!HasComp<BloodstreamComponent>(target) && !HasComp<VirologyOrganComponent>(target)
            && !_solutions.TryGetDrawableSolution(target, out _, out _)) return;
        args.Handled = true;
        if (!_skills.Check(args.User, ProfessionalSkill.Medicine, 2, serverPopup: true)) return;
        if (ent.Comp.Collected) { Fail(ent, args.User); return; }
        StartUse(ent, args.User, target);
    }
    private void OnCollected(Entity<VirologySampleComponent> ent, ref VirologyUseDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target) return;
        args.Handled = true;
        if (!_skills.Check(args.User, ProfessionalSkill.Medicine, 2, serverPopup: true)) return;
        _popup.PopupEntity(Loc.GetString(Collect(ent, target) ? "virology-collected" : "virology-failed"), ent, args.User);
    }
    private void StartUse(EntityUid item, EntityUid user, EntityUid target) =>
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, 3,
            new VirologyUseDoAfterEvent(), item, target: target, used: item)
        { NeedHand = true, BreakOnMove = true, BreakOnDamage = true, DistanceThreshold = 1.5f });

    private void OnInject(Entity<VirologyDoseComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target) return;
        args.Handled = true;
        if (!_skills.Check(args.User, ProfessionalSkill.Medicine, 1, serverPopup: true)) return;
        if (ent.Comp.Used || !_diseases.Susceptible(target)) { Fail(ent, args.User); return; }
        StartUse(ent, args.User, target);
    }
    private void OnInjected(Entity<VirologyDoseComponent> ent, ref VirologyUseDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target) return;
        args.Handled = true;
        if (!_skills.Check(args.User, ProfessionalSkill.Medicine, 1, serverPopup: true)) return;
        if (!Apply(ent, target)) { Fail(ent, args.User); return; }
        _popup.PopupEntity(Loc.GetString("virology-administered"), target, args.User);
    }
    public bool Apply(EntityUid dose, EntityUid target)
    {
        if (!TryComp<VirologyDoseComponent>(dose, out var data) || data.Used
            || !_diseases.ApplyDose(target, data.Disease, data.Vaccine)) return false;
        data.Used = true;
        _metadata.SetEntityName(dose, Loc.GetString("virology-used-dose"));
        return true;
    }
    private void OnDoseExamined(Entity<VirologyDoseComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Used) args.PushText(Loc.GetString("virology-used-dose"));
        else if (_prototypes.TryIndex<RadiantDiseasePrototype>(ent.Comp.Disease, out var disease))
            args.PushText(Loc.GetString("virology-dose-label", ("disease", Loc.GetString(disease.Name))));
        else args.PushText(Loc.GetString("virology-unconfigured"));
    }
    private void OnSampleExamined(Entity<VirologySampleComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Collected)
            args.PushText(Loc.GetString(ent.Comp.Analyzed ? "virology-sample-analyzed" : "virology-sample-pending"));
    }

    private void OnInit(Entity<VirologyMachineComponent> ent, ref ComponentInit args) =>
        ent.Comp.Sample = _containers.EnsureContainer<ContainerSlot>(ent, "virology-sample");
    public bool Powered(EntityUid uid) =>
        Transform(uid).Anchored && TryComp<ApcPowerReceiverComponent>(uid, out var power) && power.Powered;
    public bool Insert(EntityUid uid, EntityUid sample)
    {
        var machine = Comp<VirologyMachineComponent>(uid);
        if (machine.Work != null || machine.Sample.ContainedEntity != null
            || !TryComp<VirologySampleComponent>(sample, out var data) || !data.Collected)
            return false;
        return _containers.Insert(sample, machine.Sample);
    }
    private void OnInsert(Entity<VirologyMachineComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<VirologySampleComponent>(args.Used)) return;
        args.Handled = true;
        if (!Insert(ent, args.Used)) Fail(ent, args.User);
        Publish(ent);
    }
    private void OnOpened(Entity<VirologyMachineComponent> ent, ref BoundUIOpenedEvent args) => Publish(ent);
    private void Fail(EntityUid uid, EntityUid user) =>
        _popup.PopupEntity(Loc.GetString("virology-failed"), uid, user);

    private VirologySampleComponent? Sample(VirologyMachineComponent machine) =>
        machine.Sample.ContainedEntity is { } sample ? CompOrNull<VirologySampleComponent>(sample) : null;

    public bool Start(EntityUid uid, VirologyCommand command)
    {
        var machine = Comp<VirologyMachineComponent>(uid);
        var sample = Sample(machine);
        if (!Powered(uid) || machine.Work != null || sample == null) return false;
        if (command == VirologyCommand.Analyze)
        {
            if (machine.Producer || sample.Analyzed) return false;
            machine.Duration = machine.AnalysisSeconds;
        }
        else if (command is VirologyCommand.Treat or VirologyCommand.Vaccinate)
        {
            if (!machine.Producer || !sample.Analyzed || !sample.Diseases.Contains(machine.Selected)
                || !_materials.TryChangeMaterialAmount(uid, "Biomass", -machine.BiomassCost)) return false;
            machine.Duration = machine.ProductionSeconds;
        }
        else return false;
        machine.Remaining = machine.Duration;
        machine.Work = command;
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/button.ogg"), uid);
        Publish(uid);
        return true;
    }

    private void OnMessage(Entity<VirologyMachineComponent> ent, ref VirologyMessage args)
    {
        var user = args.Actor;
        if (!_interaction.InRangeUnobstructed(user, ent.Owner) || !_blocker.CanInteract(user, ent.Owner)) return;
        if (args.Command == VirologyCommand.Eject)
        {
            if (ent.Comp.Work == null && ent.Comp.Sample.ContainedEntity is { } sample)
                _containers.Remove(sample, ent.Comp.Sample);
        }
        else
        {
            if (!_skills.Check(user, ProfessionalSkill.Medicine, 3, serverPopup: true)) return;
            if (args.Command == VirologyCommand.Select)
            {
                if (ent.Comp.Work == null && Sample(ent.Comp) is { Analyzed: true } data
                    && data.Diseases.Contains(args.Disease)) ent.Comp.Selected = args.Disease;
            }
            else if (!Start(ent, args.Command)) Fail(ent, user);
        }
        Publish(ent);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<VirologyMachineComponent>();
        while (query.MoveNext(out var uid, out var machine))
        {
            if (MetaData(uid).EntityPaused)
            {
                _ambience.SetAmbience(uid, false);
                continue;
            }
            machine.UiAccumulator += frameTime;
            if (machine.UiAccumulator < 1) continue;
            var seconds = machine.UiAccumulator;
            machine.UiAccumulator = 0;
            Advance(uid, seconds);
            Publish(uid);
        }
    }
    public void Advance(EntityUid uid, float seconds)
    {
        var machine = Comp<VirologyMachineComponent>(uid);
        if (machine.Work == null) return;
        if (!Powered(uid)) return;
        var previous = (int) Math.Ceiling(machine.Remaining);
        machine.Remaining -= seconds;
        if (machine.Remaining > 0)
        {
            if ((int) Math.Ceiling(machine.Remaining) != previous) Publish(uid);
            return;
        }
        var sample = Sample(machine);
        if (sample != null && machine.Work == VirologyCommand.Analyze)
        {
            sample.Analyzed = true;
            CompareAndArchive(uid, sample);
            var report = Spawn("RadiantVirologyReportPaper", Transform(uid).Coordinates);
            _metadata.SetEntityName(report, Loc.GetString("virology-report-paper-name"));
            _paper.SetContent(report, FormattedMessage.EscapeText(Report(sample)));
        }
        else if (sample != null && sample.Diseases.Contains(machine.Selected))
        {
            var vaccine = machine.Work == VirologyCommand.Vaccinate;
            var dose = Spawn("Vaccine", Transform(uid).Coordinates);
            var data = EnsureComp<VirologyDoseComponent>(dose);
            data.Disease = machine.Selected;
            data.Vaccine = vaccine;
            _metadata.SetEntityName(dose, Loc.GetString(vaccine ? "virology-vaccine-name" : "virology-treatment-name",
                ("disease", Loc.GetString(_prototypes.Index<RadiantDiseasePrototype>(data.Disease).Name))));
        }
        machine.Work = null;
        machine.Remaining = 0;
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Machines/scan_finish.ogg"), uid);
        Publish(uid);
    }

    public string Report(VirologySampleComponent sample)
    {
        var title = Loc.GetString("virology-report-donor", ("donor", sample.Donor));
        if (!sample.Analyzed) return title + "\n" + Loc.GetString("virology-sample-pending");
        var findings = sample.Diseases.Count == 0 ? Loc.GetString("virology-negative")
            : string.Join("\n\n", sample.Diseases.Select(id =>
        {
            var disease = _prototypes.Index<RadiantDiseasePrototype>(id);
            return Loc.GetString(disease.Name) + "\n" + Loc.GetString(disease.Description)
                + "\n" + Loc.GetString("virology-route-" + disease.Route);
        }));
        return title + "\n\n" + findings + ArchiveComparison(sample);
    }

    private VirologyArchiveComponent? Archive(EntityUid machine, bool create = false)
    {
        // One archive per station; an off-station laboratory keeps its own records.
        var owner = _stations.GetOwningStation(machine) ?? machine;
        return create ? EnsureComp<VirologyArchiveComponent>(owner) : CompOrNull<VirologyArchiveComponent>(owner);
    }

    private void CompareAndArchive(EntityUid machine, VirologySampleComponent sample)
    {
        sample.SharedDiseases.Clear();
        sample.ArchiveProfile = 0;
        if (sample.Diseases.Count == 0)
        {
            sample.ArchiveMatch = VirologyArchiveMatch.Negative;
            return;
        }

        var diseases = sample.Diseases.Distinct().OrderBy(id => id, StringComparer.Ordinal).ToList();
        var archive = Archive(machine, true)!;
        var exact = archive.Records.FirstOrDefault(record => record.Diseases.SequenceEqual(diseases));
        if (exact != null)
        {
            exact.Samples++;
            exact.LastDonor = sample.Donor;
            sample.ArchiveMatch = VirologyArchiveMatch.Exact;
            sample.ArchiveProfile = exact.Number;
            return;
        }

        var related = archive.Records
            .Select(record => (Record: record, Shared: record.Diseases.Intersect(diseases).ToList()))
            .OrderByDescending(pair => pair.Shared.Count)
            .FirstOrDefault();
        sample.ArchiveMatch = related.Shared is { Count: > 0 }
            ? VirologyArchiveMatch.Related : VirologyArchiveMatch.New;
        if (sample.ArchiveMatch == VirologyArchiveMatch.Related)
        {
            sample.ArchiveProfile = related.Record.Number;
            sample.SharedDiseases.AddRange(related.Shared);
        }

        archive.Records.Add(new VirologyArchiveRecord
        {
            Number = archive.Records.Count + 1,
            Diseases = diseases,
            FirstDonor = sample.Donor,
            LastDonor = sample.Donor,
            Samples = 1,
        });
    }

    private string ArchiveComparison(VirologySampleComponent sample)
    {
        var text = sample.ArchiveMatch switch
        {
            VirologyArchiveMatch.New => Loc.GetString("virology-archive-new"),
            VirologyArchiveMatch.Exact => Loc.GetString("virology-archive-exact", ("number", sample.ArchiveProfile)),
            VirologyArchiveMatch.Related => Loc.GetString("virology-archive-related",
                ("number", sample.ArchiveProfile),
                ("diseases", string.Join(", ", sample.SharedDiseases.Select(DiseaseName)))),
            VirologyArchiveMatch.Negative => Loc.GetString("virology-archive-negative"),
            _ => "",
        };
        return text.Length == 0 ? "" : "\n\n" + text;
    }

    private string DiseaseName(string id) => Loc.GetString(_prototypes.Index<RadiantDiseasePrototype>(id).Name);

    private string ArchiveReport(EntityUid machine)
    {
        var records = Archive(machine)?.Records;
        if (records == null || records.Count == 0)
            return Loc.GetString("virology-archive-empty");
        return Loc.GetString("virology-archive-intro") + "\n\n" + string.Join("\n\n", records.Select(record =>
            Loc.GetString("virology-archive-entry", ("number", record.Number),
                ("diseases", string.Join(", ", record.Diseases.Select(DiseaseName))),
                ("samples", record.Samples), ("first", record.FirstDonor), ("last", record.LastDonor))));
    }

    public VirologyState BuildState(EntityUid uid)
    {
        var machine = Comp<VirologyMachineComponent>(uid);
        var sample = Sample(machine);
        var state = new VirologyState
        {
            Producer = machine.Producer,
            Report = sample == null ? Loc.GetString("virology-no-sample") : Report(sample)
                + (machine.Producer && !sample.Analyzed ? "\n\n" + Loc.GetString("virology-producer-needs-analysis") : ""),
            Archive = ArchiveReport(uid),
            Status = Loc.GetString(!Powered(uid) ? "virology-no-power" : machine.Work != null ? "virology-working" : "virology-idle")
                + (machine.Work != null ? Loc.GetString("virology-time", ("seconds", (int) Math.Ceiling(machine.Remaining))) : "")
                + (machine.Producer ? Loc.GetString("virology-biomass",
                    ("amount", _materials.GetMaterialAmount(uid, "Biomass")), ("cost", machine.BiomassCost)) : ""),
            CanEject = machine.Work == null && sample != null,
            CanAnalyze = Powered(uid) && !machine.Producer && machine.Work == null && sample is { Analyzed: false },
            CanProduce = Powered(uid) && machine.Producer && machine.Work == null && sample is { Analyzed: true }
                && sample.Diseases.Count > 0 && _materials.GetMaterialAmount(uid, "Biomass") >= machine.BiomassCost,
            Working = machine.Work != null,
            Progress = machine.Work == null ? 0 : Math.Clamp(1 - machine.Remaining / Math.Max(1, machine.Duration), 0, 1)
        };
        if (sample is { Analyzed: true })
            foreach (var id in sample.Diseases)
                state.Diseases[id] = Loc.GetString(_prototypes.Index<RadiantDiseasePrototype>(id).Name);
        if (!state.Diseases.ContainsKey(machine.Selected))
            machine.Selected = state.Diseases.Keys.FirstOrDefault() ?? "";
        state.Selected = machine.Selected;
        return state;
    }
    private void Publish(EntityUid uid)
    {
        var powered = Powered(uid);
        var running = powered && Comp<VirologyMachineComponent>(uid).Work != null;
        _visuals.SetData(uid, VirologyMachineVisuals.Powered, powered);
        _visuals.SetData(uid, VirologyMachineVisuals.Running, running);
        _ambience.SetAmbience(uid, running);
        _ui.SetUiState(uid, VirologyUiKey.Key, BuildState(uid));
    }
}

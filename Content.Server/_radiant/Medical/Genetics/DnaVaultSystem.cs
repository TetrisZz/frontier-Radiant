using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Content.Server.Botany.Components;
using Content.Server.Power.Components;
using Content.Shared.ActionBlocker;
using Content.Shared.Body.Components;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Forensics.Components;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Materials;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared._radiant.Medical.Genetics;
using Content.Shared._radiant.Skills;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._radiant.Medical.Genetics;

[DataDefinition]
public sealed partial class BiodiversityRecord
{
    [DataField] public string Key = "";
    [DataField] public string Category = "";
    [DataField] public string Label = "";
}
[RegisterComponent]
public sealed partial class BiodiversityProbeComponent : Component
{
    [DataField] public List<BiodiversityRecord> Records = new();
    [DataField] public int Capacity = 32;
}
[RegisterComponent]
public sealed partial class DnaVaultComponent : Component
{
    [DataField] public int HumanGoal = 3;
    [DataField] public int PlantGoal = 3;
    [DataField] public int AnimalGoal = 3;
    [DataField] public bool RandomizeGoals = true;
    [DataField] public int MinimumSamples = 2;
    [DataField] public int MaximumSamples = 5;
    [DataField] public int BiomassCost = 60;
    [DataField] public float PreparationSeconds = 180;
    [DataField] public List<string> Rewards = new()
        { "GeneticHematopoiesisInjector", "GeneticCoagulationInjector", "GeneticRegenerationInjector", "GeneticSobrietyInjector" };
    [DataField] public string Epoch = "";
    [DataField] public Dictionary<string, BiodiversityRecord> Records = new();
    public ContainerSlot Sample = default!;
    public GeneticSampleComponent? Order;
    public string Product = "";
    public float Remaining, Refresh;
}

/// <summary>Optional local collection, never a station goal. Entitlement is global per DNA and round.</summary>
public sealed class DnaVaultSystem : EntitySystem
{
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private SharedMaterialStorageSystem _materials = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedProfessionalSkillsSystem _skills = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private IRobustRandom _random = default!;
    private string _epoch = Guid.NewGuid().ToString();
    private readonly HashSet<string> _claimed = new();
    private readonly Dictionary<string, EntityUid> _reserved = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<BiodiversityProbeComponent, AfterInteractEvent>(OnScan);
        SubscribeLocalEvent<BiodiversityProbeComponent, BiodiversityScanEvent>(OnScanned);
        SubscribeLocalEvent<BiodiversityProbeComponent, ExaminedEvent>(OnProbeExamine);
        SubscribeLocalEvent<DnaVaultComponent, ComponentInit>(OnInit);
        SubscribeLocalEvent<DnaVaultComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<DnaVaultComponent, InteractUsingEvent>(OnInsert);
        SubscribeLocalEvent<DnaVaultComponent, BoundUIOpenedEvent>(OnOpened);
        SubscribeLocalEvent<DnaVaultComponent, DnaVaultMessage>(OnMessage);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => { _epoch = Guid.NewGuid().ToString(); _claimed.Clear(); _reserved.Clear(); });
    }

    private void OnInit(Entity<DnaVaultComponent> ent, ref ComponentInit args)
        => ent.Comp.Sample = _containers.EnsureContainer<ContainerSlot>(ent, "dna-vault-sample");
    private static string Identity(GeneticSampleComponent sample) => sample.Species + ":" + sample.Dna;
    private void EnsureRound(DnaVaultComponent vault)
    {
        if (vault.Epoch == _epoch) return;
        vault.Epoch = _epoch;
        ResetCollection(vault);
        vault.Order = null;
        vault.Remaining = 0;
    }

    private void ResetCollection(DnaVaultComponent vault)
    {
        vault.Records.Clear();
        if (!vault.RandomizeGoals) return;
        // Three distinct, bounded requirements; never repeat the previous collection's exact assignment.
        var minimum = Math.Clamp(vault.MinimumSamples, 1, 6);
        var maximum = Math.Clamp(vault.MaximumSamples, minimum + 2, 8);
        var pool = Enumerable.Range(minimum, maximum - minimum + 1).ToList();
        var goals = new int[3];
        for (var i = 0; i < goals.Length; i++)
        {
            var index = _random.Next(pool.Count);
            goals[i] = pool[index];
            pool.RemoveAt(index);
        }
        if (goals[0] == vault.HumanGoal && goals[1] == vault.PlantGoal && goals[2] == vault.AnimalGoal)
            (goals[0], goals[1]) = (goals[1], goals[0]);
        vault.HumanGoal = goals[0];
        vault.PlantGoal = goals[1];
        vault.AnimalGoal = goals[2];
    }
    private void OnShutdown(Entity<DnaVaultComponent> ent, ref ComponentShutdown args)
    {
        if (ent.Comp.Order is { } order && _reserved.GetValueOrDefault(Identity(order)) == ent.Owner)
            _reserved.Remove(Identity(order));
    }
    private BiodiversityRecord? Record(EntityUid target)
    {
        if (TryComp<ProduceComponent>(target, out var plant) && plant.SeedId is { } seed)
            return new() { Key = "plant:" + seed, Category = "plant", Label = Name(target) };
        if (!HasComp<BloodstreamComponent>(target) || !HasComp<HungerComponent>(target)) return null;
        if (TryComp<HumanoidAppearanceComponent>(target, out var look))
            return look.Species.Id is not ("IPC" or "SlimePerson") && TryComp<DnaComponent>(target, out var dna)
                && !string.IsNullOrWhiteSpace(dna.DNA)
                ? new() { Key = "human:" + look.Species.Id + ":" + dna.DNA, Category = "human", Label = Name(target) } : null;
        if (MetaData(target).EntityPrototype is not { } prototype) return null;
        var ancestor = _prototypes.EnumerateParents<EntityPrototype>(prototype.ID)
            .Where(p => !p.Abstract && p.Components.ContainsKey("Bloodstream")).LastOrDefault();
        return new() { Key = "animal:" + (ancestor?.ID ?? prototype.ID), Category = "animal", Label = Name(target) };
    }
    public bool Scan(EntityUid probe, EntityUid target)
    {
        if (!TryComp<BiodiversityProbeComponent>(probe, out var data) || data.Records.Count >= data.Capacity
            || Record(target) is not { } record || data.Records.Any(r => r.Key == record.Key)) return false;
        data.Records.Add(record);
        return true;
    }
    private void OnScan(Entity<BiodiversityProbeComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || Record(target) == null) return;
        args.Handled = true;
        if (!_skills.Check(args.User, ProfessionalSkill.Medicine, 2, serverPopup: true)) return;
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, 2, new BiodiversityScanEvent(), ent,
            target: target, used: ent) { NeedHand = true, BreakOnMove = true, BreakOnDamage = true });
    }
    private void OnScanned(Entity<BiodiversityProbeComponent> ent, ref BiodiversityScanEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target
            || !_skills.Check(args.User, ProfessionalSkill.Medicine, 2, serverPopup: true)) return;
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString(Scan(ent, target) ? "dna-vault-scanned" : "dna-vault-duplicate"), ent, args.User);
    }
    private void OnProbeExamine(Entity<BiodiversityProbeComponent> ent, ref ExaminedEvent args)
        => args.PushText(Loc.GetString("dna-vault-probe-count", ("count", ent.Comp.Records.Count), ("capacity", ent.Comp.Capacity)));
    public int Upload(EntityUid uid, EntityUid probe)
    {
        if (!TryComp<DnaVaultComponent>(uid, out var vault) || !Powered(uid) || vault.Order != null
            || !TryComp<BiodiversityProbeComponent>(probe, out var data)) return 0;
        EnsureRound(vault);
        var added = 0;
        foreach (var record in data.Records.ToArray())
        {
            var goal = record.Category switch { "human" => vault.HumanGoal, "plant" => vault.PlantGoal, "animal" => vault.AnimalGoal, _ => 0 };
            if (vault.Records.Values.Count(r => r.Category == record.Category) >= goal || vault.Records.ContainsKey(record.Key)) continue;
            vault.Records.Add(record.Key, new() { Key = record.Key, Category = record.Category, Label = record.Label });
            data.Records.Remove(record);
            added++;
        }
        return added;
    }
    public bool Insert(EntityUid uid, EntityUid sample)
    {
        if (!TryComp<DnaVaultComponent>(uid, out var vault) || vault.Order != null || vault.Sample.ContainedEntity != null
            || !TryComp<GeneticSampleComponent>(sample, out var data) || !data.Analyzed || !data.TherapyCompatible
            || string.IsNullOrEmpty(data.Dna)) return false;
        return _containers.Insert(sample, vault.Sample);
    }
    private void OnInsert(Entity<DnaVaultComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<BiodiversityProbeComponent>(args.Used) && !HasComp<GeneticSampleComponent>(args.Used)) return;
        args.Handled = true;
        if (!_skills.Check(args.User, ProfessionalSkill.Medicine, 3, serverPopup: true)) return;
        var success = HasComp<BiodiversityProbeComponent>(args.Used) ? Upload(ent, args.Used) > 0 : Insert(ent, args.Used);
        _popup.PopupEntity(Loc.GetString(success ? "dna-vault-loaded" : "dna-vault-insert-help"), ent, args.User);
        Publish(ent);
    }
    private bool Powered(EntityUid uid) => Transform(uid).Anchored
        && TryComp<ApcPowerReceiverComponent>(uid, out var power) && power.Powered;
    private bool Complete(DnaVaultComponent vault) => vault.Records.Values.Count(r => r.Category == "human") >= vault.HumanGoal
        && vault.Records.Values.Count(r => r.Category == "plant") >= vault.PlantGoal
        && vault.Records.Values.Count(r => r.Category == "animal") >= vault.AnimalGoal;
    private string[] Options(DnaVaultComponent vault, GeneticSampleComponent sample)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(_epoch + Identity(sample)));
        return vault.Rewards.Distinct().Where(id => _prototypes.TryIndex<EntityPrototype>(id, out var proto)
            && proto.TryGetComponent<GeneticTherapyComponent>(out var therapy, EntityManager.ComponentFactory)
            && !therapy.Remove).OrderBy(id => Convert.ToHexString(SHA256.HashData(bytes.Concat(Encoding.UTF8.GetBytes(id)).ToArray())))
            .Take(2).ToArray();
    }
    public bool Prepare(EntityUid uid, string reward)
    {
        var vault = Comp<DnaVaultComponent>(uid);
        EnsureRound(vault);
        if (!Powered(uid) || !Complete(vault) || vault.Order != null
            || vault.Sample.ContainedEntity is not { } cartridge || !TryComp<GeneticSampleComponent>(cartridge, out var sample)
            || !sample.Analyzed || !sample.TherapyCompatible || _claimed.Contains(Identity(sample))
            || _reserved.ContainsKey(Identity(sample)) || !Options(vault, sample).Contains(reward)
            || !_materials.TryChangeMaterialAmount(uid, "Biomass", -vault.BiomassCost)) return false;
        vault.Order = new() { Species = sample.Species, Dna = sample.Dna, Donor = sample.Donor };
        vault.Product = reward;
        vault.Remaining = vault.PreparationSeconds;
        _reserved[Identity(sample)] = uid;
        return true;
    }
    public void Advance(EntityUid uid, float seconds)
    {
        var vault = Comp<DnaVaultComponent>(uid);
        EnsureRound(vault);
        if (seconds <= 0 || !Powered(uid) || vault.Order is not { } order) return;
        vault.Remaining -= seconds;
        if (vault.Remaining > 0) return;
        _reserved.Remove(Identity(order));
        if (_claimed.Add(Identity(order)))
        {
            foreach (var id in new[] { vault.Product, vault.Product.Replace("Injector", "Remover") })
            {
                var item = Spawn(id, Transform(uid).Coordinates);
                var therapy = Comp<GeneticTherapyComponent>(item);
                therapy.Dna = order.Dna;
                therapy.Species = order.Species;
                EntityManager.System<MetaDataSystem>().SetEntityName(item,
                    Loc.GetString("genetics-therapy-labelled", ("name", Name(item)), ("donor", order.Donor)));
            }
            ResetCollection(vault);
        }
        vault.Order = null;
        vault.Remaining = 0;
    }
    private void OnMessage(Entity<DnaVaultComponent> ent, ref DnaVaultMessage args)
    {
        if (!_interaction.InRangeUnobstructed(args.Actor, ent.Owner) || !_blocker.CanInteract(args.Actor, ent.Owner)) return;
        if (args.Eject && ent.Comp.Order == null && ent.Comp.Sample.ContainedEntity is { } sample)
            _containers.Remove(sample, ent.Comp.Sample);
        else if (!args.Eject && _skills.Check(args.Actor, ProfessionalSkill.Medicine, 4, serverPopup: true)
            && !Prepare(ent, args.Reward))
            _popup.PopupEntity(Loc.GetString("dna-vault-unavailable"), ent, args.Actor);
        Publish(ent);
    }
    public DnaVaultState BuildState(EntityUid uid)
    {
        var vault = Comp<DnaVaultComponent>(uid);
        EnsureRound(vault);
        var sample = vault.Sample.ContainedEntity is { } cartridge ? CompOrNull<GeneticSampleComponent>(cartridge) : null;
        var claimed = sample != null && (_claimed.Contains(Identity(sample)) || _reserved.ContainsKey(Identity(sample)));
        var state = new DnaVaultState
        {
            Humans = vault.Records.Values.Count(r => r.Category == "human"), HumanGoal = vault.HumanGoal,
            Plants = vault.Records.Values.Count(r => r.Category == "plant"), PlantGoal = vault.PlantGoal,
            Animals = vault.Records.Values.Count(r => r.Category == "animal"), AnimalGoal = vault.AnimalGoal,
            Biomass = _materials.GetMaterialAmount(uid, "Biomass"), Cost = vault.BiomassCost,
            Sample = sample == null ? Loc.GetString("dna-vault-patient-help") : sample.Donor + "\n" + sample.ProfileSnapshot,
            Status = Loc.GetString(!Powered(uid) ? "virology-no-power" : vault.Order != null ? "dna-vault-preparing"
                : claimed ? "dna-vault-claimed" : Complete(vault) ? "dna-vault-ready" : "dna-vault-collecting"),
            CanEject = sample != null && vault.Order == null,
            CanPrepare = Powered(uid) && Complete(vault) && !claimed && sample is { Analyzed: true, TherapyCompatible: true }
                && vault.Order == null && _materials.GetMaterialAmount(uid, "Biomass") >= vault.BiomassCost,
            Progress = vault.Order == null ? 0 : Math.Clamp(1 - vault.Remaining / Math.Max(1, vault.PreparationSeconds), 0, 1),
            Archive = string.Join("\n", vault.Records.Values.Select(r => r.Label)),
        };
        if (sample != null && Complete(vault))
            foreach (var id in Options(vault, sample))
            {
                var proto = _prototypes.Index<EntityPrototype>(id);
                proto.TryGetComponent<GeneticTherapyComponent>(out var therapy, EntityManager.ComponentFactory);
                var gene = _prototypes.Index(therapy!.Modification);
                state.Rewards[id] = Loc.GetString(gene.Name) + "\n" + Loc.GetString("dna-vault-reward-load", ("load", gene.Load));
            }
        return state;
    }
    private void OnOpened(Entity<DnaVaultComponent> ent, ref BoundUIOpenedEvent args) => Publish(ent);
    private void Publish(EntityUid uid) => _ui.SetUiState(uid, DnaVaultUiKey.Key, BuildState(uid));
    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<DnaVaultComponent>();
        while (query.MoveNext(out var uid, out var vault))
        {
            if (MetaData(uid).EntityPaused) continue;
            Advance(uid, frameTime);
            vault.Refresh -= frameTime;
            if (vault.Refresh > 0) continue;
            vault.Refresh = 1;
            Publish(uid);
        }
    }
}

using System.Linq;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Forensics.Components;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared._radiant.Medical.Genetics;
using Content.Shared._radiant.Skills;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Medical.Genetics;

/// <summary>Belongs to the physical body, never copied by brain identity restoration.</summary>
[RegisterComponent]
public sealed partial class GeneticProfileComponent : Component
{
    [DataField] public int Capacity = 100;
    // Remaining adaptation seconds; zero denotes an active modification.
    [DataField] public Dictionary<string, float> Modifications = new();
    public float Accumulator;
    [DataField] public Dictionary<string, string> Complications = new();
    public Dictionary<Type, Component> OwnedEffects = new();
    public float TicTimer = 120;
}

[RegisterComponent]
public sealed partial class GeneticTherapyComponent : Component
{
    [DataField] public ProtoId<GeneticModificationPrototype> Modification = "RadiantHematopoiesis";
    [DataField] public bool Remove;
    [DataField] public bool Used;
    [DataField] public string? Dna;
    [DataField] public string? Species;
}

public sealed partial class GeneticModificationSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedDoAfterSystem _doAfter = default!;
    [Dependency] private SharedProfessionalSkillsSystem _skills = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedBloodstreamSystem _blood = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private HungerSystem _hunger = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private SharedAppearanceSystem _visuals = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<GeneticTherapyComponent, AfterInteractEvent>(OnInteract);
        SubscribeLocalEvent<GeneticTherapyComponent, GeneticTherapyDoAfterEvent>(OnInjected);
        SubscribeLocalEvent<GeneticTherapyComponent, ExaminedEvent>(OnExamine);
    }

    public int Load(GeneticProfileComponent profile)
        => profile.Modifications.Keys.Sum(id => _prototypes.TryIndex<GeneticModificationPrototype>(id, out var gene) ? gene.Load : 0);

    public string ProfileText(EntityUid patient)
    {
        if (!TryComp<GeneticProfileComponent>(patient, out var profile) || profile.Modifications.Count == 0)
            return Loc.GetString("genetics-profile-baseline");
        var lines = new List<string>
        {
            Loc.GetString("genetics-profile-load", ("load", Load(profile)), ("capacity", profile.Capacity)),
        };
        foreach (var (id, seconds) in profile.Modifications.OrderBy(p => p.Key))
        {
            if (!_prototypes.TryIndex<GeneticModificationPrototype>(id, out var gene))
                continue;
            lines.Add(Loc.GetString(seconds > 0 ? "genetics-profile-adapting" : "genetics-profile-active",
                ("name", Loc.GetString(gene.Name)), ("seconds", (int) Math.Ceiling(seconds))));
            if (seconds <= 0 && profile.Complications.TryGetValue(id, out var flaw))
                lines.Add(Loc.GetString("genetics-complication-" + flaw));
        }
        return string.Join("\n", lines);
    }

    public string? Failure(EntityUid injector, EntityUid patient)
    {
        if (!TryComp<GeneticTherapyComponent>(injector, out var therapy) || therapy.Used)
            return "genetics-therapy-spent";
        if (!TryComp<HumanoidAppearanceComponent>(patient, out var look)
            || !HasComp<BloodstreamComponent>(patient) || !HasComp<HungerComponent>(patient)
            || !TryComp<DnaComponent>(patient, out var dna) || _mobs.IsDead(patient))
            return "genetics-therapy-incompatible";
        if (therapy.Dna != null && therapy.Dna != dna.DNA
            || therapy.Species != null && therapy.Species != look.Species.Id)
            return "genetics-therapy-wrong-donor";
        var profile = CompOrNull<GeneticProfileComponent>(patient);
        var present = profile?.Modifications.ContainsKey(therapy.Modification.Id) == true;
        if (therapy.Remove)
            return present ? null : "genetics-therapy-not-present";
        if (present)
            return "genetics-therapy-already-present";
        var gene = _prototypes.Index(therapy.Modification);
        if (profile != null && profile.Modifications.Keys.Any(id =>
                gene.Conflicts.Contains(id) || _prototypes.Index<GeneticModificationPrototype>(id).Conflicts.Contains(gene.ID)))
            return "genetics-therapy-conflict";
        if ((profile == null ? 0 : Load(profile)) + gene.Load > (profile?.Capacity ?? 100))
            return "genetics-therapy-overload";
        return null;
    }

    /// <summary>Apply exactly once; callers perform interaction and medical-skill checks.</summary>
    public bool Apply(EntityUid injector, EntityUid patient)
    {
        if (Failure(injector, patient) != null)
            return false;
        var therapy = Comp<GeneticTherapyComponent>(injector);
        var profile = EnsureComp<GeneticProfileComponent>(patient);
        if (therapy.Remove)
        {
            profile.Modifications.Remove(therapy.Modification.Id);
            profile.Complications.Remove(therapy.Modification.Id);
        }
        else
        {
            profile.Modifications.Add(therapy.Modification.Id, _prototypes.Index(therapy.Modification).AdaptationSeconds);
            RollComplication(profile, _prototypes.Index(therapy.Modification));
        }
        RefreshGeneticEffects(patient, profile);
        therapy.Used = true;
        _visuals.SetData(injector, GeneticTherapyVisuals.Used, true);
        _metadata.SetEntityName(injector, Loc.GetString("genetics-therapy-used-name", ("name", Name(injector))));
        return true;
    }

    private void OnExamine(Entity<GeneticTherapyComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Used)
            args.PushText(Loc.GetString("genetics-therapy-spent"));
        else if (ent.Comp.Dna != null)
            args.PushText(Loc.GetString("genetics-therapy-personal"));
    }

    private void OnInteract(Entity<GeneticTherapyComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } patient)
            return;
        args.Handled = true;
        if (!_skills.Check(args.User, ProfessionalSkill.Medicine, 4, serverPopup: true))
            return;
        if (Failure(ent, patient) is { } reason)
        {
            _popup.PopupEntity(Loc.GetString(reason), patient, args.User);
            return;
        }
        _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User, 5,
            new GeneticTherapyDoAfterEvent(), ent, target: patient, used: ent)
        {
            NeedHand = true, BreakOnMove = true, BreakOnDamage = true, DistanceThreshold = 1.5f,
        });
    }

    private void OnInjected(Entity<GeneticTherapyComponent> ent, ref GeneticTherapyDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } patient)
            return;
        args.Handled = true;
        if (!_skills.Check(args.User, ProfessionalSkill.Medicine, 4, serverPopup: true))
            return;
        if (Failure(ent, patient) is { } reason)
        {
            _popup.PopupEntity(Loc.GetString(reason), patient, args.User);
            return;
        }
        if (Apply(ent, patient))
            _popup.PopupEntity(Loc.GetString(ent.Comp.Remove ? "genetics-therapy-removed" : "genetics-therapy-applied"), patient, args.User);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<GeneticProfileComponent>();
        while (query.MoveNext(out var uid, out var profile))
        {
            if (MetaData(uid).EntityPaused)
                continue;
            profile.Accumulator += frameTime;
            if (profile.Accumulator < 1)
                continue;
            var seconds = profile.Accumulator;
            profile.Accumulator = 0;
            Advance(uid, seconds);
        }
    }

    public void Advance(EntityUid uid, float seconds)
    {
        if (seconds <= 0 || _mobs.IsDead(uid) || !TryComp<GeneticProfileComponent>(uid, out var profile))
            return;
        foreach (var id in profile.Modifications.Keys.ToArray())
        {
            if (!_prototypes.TryIndex<GeneticModificationPrototype>(id, out var gene))
                continue;
            var pending = profile.Modifications[id];
            profile.Modifications[id] = Math.Max(0, pending - seconds);
            var activeSeconds = Math.Max(0, seconds - pending);
            if (activeSeconds <= 0 || Load(profile) > profile.Capacity
                || !TryComp<BloodstreamComponent>(uid, out var blood)
                || !TryComp<HungerComponent>(uid, out var hunger)
                || _hunger.GetHungerThreshold(hunger) is HungerThreshold.Starving or HungerThreshold.Dead)
                continue;
            // Deliberate upkeep for tissue repair and clotting. Never revives, restores organs or treats infection.
            if (gene.NutritionPerSecond > 0)
            {
                var upkeepSeconds = Math.Min(activeSeconds, Math.Max(0, _hunger.GetHunger(hunger)) / gene.NutritionPerSecond);
                var worked = false;
                if (gene.ClottingPerSecond > 0 && blood.BleedAmount > 0 && upkeepSeconds > 0)
                {
                    _blood.TryModifyBleedAmount((uid, blood), -gene.ClottingPerSecond * upkeepSeconds);
                    worked = true;
                }
                if (gene.HealingPerSecond > 0 && upkeepSeconds > 0 && TryComp<DamageableComponent>(uid, out var damaged))
                {
                    var healing = new DamageSpecifier();
                    foreach (var type in new[] { "Blunt", "Slash", "Piercing" })
                        if (damaged.Damage.DamageDict.TryGetValue(type, out var value) && value > 0)
                            healing.DamageDict[type] = -FixedPoint2.New(Math.Min(value.Float(), gene.HealingPerSecond * upkeepSeconds));
                    if (healing.DamageDict.Count > 0)
                    {
                        _damage.TryChangeDamage(uid, healing, ignoreResistances: true, interruptsDoAfters: false);
                        worked = true;
                    }
                }
                if (worked)
                    _hunger.ModifyHunger(uid, -gene.NutritionPerSecond * upkeepSeconds, hunger);
            }
            var solution = blood.BloodSolution;
            if (!_solutions.ResolveSolution(uid, blood.BloodSolutionName, ref solution, out var contents))
                continue;
            var missing = (contents.MaxVolume - contents.Volume).Float();
            var amount = Math.Min(missing, gene.BloodPerSecond * activeSeconds);
            if (amount <= 0)
                continue;
            // Bound the effect by available nutrition, including long server frames.
            if (gene.NutritionPerBlood > 0)
                amount = Math.Min(amount, Math.Max(0, _hunger.GetHunger(hunger)) / gene.NutritionPerBlood);
            var added = FixedPoint2.New(amount);
            if (added > 0 && _blood.TryModifyBloodLevel((uid, blood), added))
                _hunger.ModifyHunger(uid, -added.Float() * gene.NutritionPerBlood, hunger);
        }
        RefreshGeneticEffects(uid, profile);
        AdvanceComplications(uid, profile, seconds);
    }
}

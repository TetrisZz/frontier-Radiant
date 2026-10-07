using System.Linq;
using Content.Shared._radiant.Addictions;
using Content.Shared._radiant;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.DetailExaminable;
using Content.Shared.ERP.Components;
using Content.Shared.EntityEffects;
using Content.Shared.EntityEffects.Effects;
using Content.Shared.FixedPoint;
using Content.Shared.Jittering;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Rejuvenate;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Addictions;

[RegisterComponent]
public sealed partial class AddictionComponent : Component
{
    [DataField] public Dictionary<string, AddictionState> Groups = new();
    [DataField] public float SymptomTimer;
    [DataField] public float Speed = 1;
}

/// <summary>Server-owned round state. No profile or database writes.</summary>
public sealed partial class AddictionSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedJitteringSystem _jitter = default!;
    [Dependency] private DamageableSystem _damage = default!;
    private float _elapsed;

    public override void Initialize()
    {
        SubscribeLocalEvent<AddictionComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);
        SubscribeLocalEvent<AddictionComponent, RejuvenateEvent>(OnRejuvenate);
        SubscribeLocalEvent<AddictionComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<AddictionHistoryComponent, MapInitEvent>(OnHistoryInit);
        SubscribeLocalEvent<PermanentHabitComponent, RejuvenateEvent>(OnHabitRejuvenate);
    }

    private void OnHistoryInit(Entity<AddictionHistoryComponent> ent, ref MapInitEvent args)
    {
        if (!_prototypes.TryIndex<AddictionGroupPrototype>(ent.Comp.Group, out var group) || !Allowed(ent, group))
            return;
        var addictions = EnsureComp<AddictionComponent>(ent);
        // Do not overwrite existing progress when a history component is copied or added again.
        if (addictions.Groups.ContainsKey(group.ID))
            return;
        addictions.Groups[group.ID] = new AddictionState
        {
            Dependence = Math.Clamp(ent.Comp.Dependence, 0, 100),
            Tolerance = Math.Clamp(ent.Comp.Tolerance, 0, 100),
            SecondsWithoutDose = ent.Comp.Recovering ? group.GraceSeconds : 0,
        };
    }

    private void OnSpeed(Entity<AddictionComponent> ent, ref RefreshMovementSpeedModifiersEvent args) => args.ModifySpeed(ent.Comp.Speed);
    private void OnShutdown(Entity<AddictionComponent> ent, ref ComponentShutdown args)
    {
        SetVisuals(ent, 0);
        ent.Comp.Speed = 1;
        _movement.RefreshMovementSpeedModifiers(ent);
    }
    private void OnRejuvenate(Entity<AddictionComponent> ent, ref RejuvenateEvent args)
    {
        SetVisuals(ent, 0);
        ent.Comp.Groups.Clear();
        ent.Comp.Speed = 1;
        _movement.RefreshMovementSpeedModifiers(ent);
    }

    private bool Allowed(EntityUid uid, AddictionGroupPrototype group) => !group.RequiresErp
        || !TryComp<DetailExaminableComponent>(uid, out var detail) || detail.ERPStatus != EnumERPStatus.NO;

    public AddictionGroupPrototype? FindGroup(ReagentPrototype reagent)
    {
        foreach (var group in _prototypes.EnumeratePrototypes<AddictionGroupPrototype>())
        {
            if (group.Reagents.Contains(reagent.ID))
                return group;
            // Drinks share the alcohol group based on their actual effect, not their display name.
            if (group.Alcohol && reagent.Metabolisms != null
                && reagent.Metabolisms.Values.Any(entry => entry.Effects.Any(effect => effect is Content.Shared.EntityEffects.Effects.Drunk)))
                return group;
        }
        return null;
    }

    public void RegisterDose(EntityUid uid, ReagentPrototype reagent, float dose)
    {
        if (!HasComp<MobStateComponent>(uid) || _mobs.IsDead(uid) || dose <= 0)
            return;
        var group = FindGroup(reagent);
        if (group == null || !Allowed(uid, group))
            return;
        var component = EnsureComp<AddictionComponent>(uid);
        if (!component.Groups.TryGetValue(group.ID, out var state))
            component.Groups[group.ID] = state = new AddictionState();
        var dependence = state.Dependence;
        var tolerance = state.Tolerance;
        AddictionRules.Dose(state, group, dose);
        if (group.ID is "nicotine" or "alcohol" or "excitement"
            && EntityManager.System<Content.Server._radiant.Medical.Genetics.GeneticModificationSystem>().HasActive(uid, "RadiantSobriety"))
        {
            // Prevent new habituation, not intoxication, poisoning or pre-existing dependence.
            state.Dependence = dependence;
            state.Tolerance = tolerance;
        }
        if (TryComp<PermanentHabitComponent>(uid, out var habit) && habit.Group == group.ID)
        {
            habit.SecondsWithoutDose = 0;
            habit.MessageTimer = 0;
        }
        RefreshSpeed(uid, component, HasTreatment(uid));
    }

    public void SetTolerance(EntityEffectReagentArgs args, ReagentPrototype reagent, string metabolism)
    {
        // Duration and unrelated effects remain unchanged. Damage effects explicitly opt in.
        var group = FindGroup(reagent);
        if (group == null || !Allowed(args.TargetEntity, group)
            || !TryComp<AddictionComponent>(args.TargetEntity, out var component)
            || !component.Groups.TryGetValue(group.ID, out var state))
            return;
        var strength = AddictionRules.Strength(state.Tolerance);
        args.AddictionHealing = reagent.AddictionToleranceHealing ? strength : 1;
        args.AddictionDamage = reagent.AddictionToleranceDamage ? strength : 1;
        args.AddictionSlowdown = reagent.AddictionToleranceSlowdown ? strength : 1;
    }

    private bool HasTreatment(EntityUid uid) => TryComp<BloodstreamComponent>(uid, out var blood)
        && _solutions.TryGetSolution(uid, blood.ChemicalSolutionName, out var solution)
        && solution.Value.Comp.Solution.GetTotalPrototypeQuantity("Haloperidol") >= FixedPoint2.New(0.5);

    private void RefreshSpeed(EntityUid uid, AddictionComponent component, bool treatment)
    {
        var speed = 1f;
        var visual = 0f;
        foreach (var (id, state) in component.Groups)
        {
            var group = _prototypes.Index<AddictionGroupPrototype>(id);
            if (!Allowed(uid, group))
                continue;
            var stage = Math.Max(0, state.WithdrawalStage - (treatment ? 2 : 0));
            speed = Math.Min(speed, 1 - Math.Max(0, stage - 1) * group.SpeedPenalty);
            visual = Math.Max(visual, Math.Max(0, stage - 1) / 3f);
        }
        SetVisuals(uid, visual);
        speed = Math.Max(.65f, speed);
        if (component.Speed == speed)
            return;
        component.Speed = speed;
        _movement.RefreshMovementSpeedModifiers(uid);
    }

    private void SetVisuals(EntityUid uid, float intensity)
    {
        if (intensity == 0 && !HasComp<WithdrawalVisualsComponent>(uid))
            return;
        var visuals = EnsureComp<WithdrawalVisualsComponent>(uid);
        if (visuals.Intensity == intensity)
            return;
        visuals.Intensity = intensity;
        Dirty(uid, visuals);
    }

    private void OnHabitRejuvenate(Entity<PermanentHabitComponent> ent, ref RejuvenateEvent args)
    {
        // Heal acquired illness, but do not erase a selected character habit.
        ent.Comp.SecondsWithoutDose = 0;
        ent.Comp.MessageTimer = 0;
    }

    private void UpdateHabits(float seconds)
    {
        var query = EntityQueryEnumerator<PermanentHabitComponent>();
        while (query.MoveNext(out var uid, out var habit))
        {
            if (MetaData(uid).EntityPaused || _mobs.IsDead(uid))
                continue;
            habit.SecondsWithoutDose += seconds;
            if (habit.SecondsWithoutDose < habit.CravingDelay)
                continue;
            habit.MessageTimer += seconds;
            if (habit.MessageTimer < habit.MessageInterval)
                continue;
            habit.MessageTimer = 0;
            // Avoid duplicate messages when the real illness already produces symptoms.
            if (HasTreatment(uid) || (TryComp<AddictionComponent>(uid, out var addiction)
                && addiction.Groups.TryGetValue(habit.Group, out var state) && state.WithdrawalStage > 0))
                continue;
            _popup.PopupEntity(Loc.GetString($"addiction-habit-{habit.Group}"), uid, uid, PopupType.Small);
        }
    }

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 1)
            return;
        var seconds = Math.Min(_elapsed, 5);
        _elapsed = 0;
        UpdateHabits(seconds);
        var query = EntityQueryEnumerator<AddictionComponent>();
        while (query.MoveNext(out var uid, out var component))
        {
            if (_mobs.IsDead(uid))
            {
                SetVisuals(uid, 0);
                continue;
            }
            if (MetaData(uid).EntityPaused)
                continue;
            var treatment = HasTreatment(uid);
            component.SymptomTimer += seconds;
            var showSymptoms = component.SymptomTimer >= 60;
            if (showSymptoms)
                component.SymptomTimer = 0;
            var messages = new List<string>();
            var poison = 0f;
            foreach (var (id, state) in component.Groups.ToArray())
            {
                var group = _prototypes.Index<AddictionGroupPrototype>(id);
                if (!Allowed(uid, group))
                {
                    component.Groups.Remove(id);
                    continue;
                }
                AddictionRules.Advance(state, group, seconds, treatment);
                if (state.Intoxication > group.ToxicThreshold)
                {
                    poison += Math.Min(.5f, (state.Intoxication - group.ToxicThreshold) * .02f) * seconds;
                    if (showSymptoms && !messages.Contains(Loc.GetString("addiction-intoxication")))
                        messages.Add(Loc.GetString("addiction-intoxication"));
                }
                var stage = Math.Max(0, state.WithdrawalStage - (treatment ? 2 : 0));
                if (!showSymptoms || stage == 0)
                    continue;
                messages.Add(Loc.GetString($"addiction-craving-{id}") + " " + Loc.GetString($"addiction-stage-{stage}"));
                if (stage >= 2 && group.Tremor > 0)
                    _jitter.DoJitter(uid, TimeSpan.FromSeconds(stage * 2), true, group.Tremor * stage, 3);
            }
            if (poison > 0)
                _damage.TryChangeDamage(uid, new DamageSpecifier { DamageDict = new() { ["Poison"] = FixedPoint2.New(Math.Min(poison, seconds)) } }, interruptsDoAfters: false);
            if (messages.Count > 0)
                _popup.PopupEntity(string.Join("\n", messages), uid, uid, PopupType.MediumCaution);
            RefreshSpeed(uid, component, treatment);
        }
    }
}

using System.Linq;
using Content.Server.Body.Systems;
using Content.Server.Chemistry.Containers.EntitySystems;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Speech.Muting;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Medical.Surgery;

/// <summary>
/// Radiant sector: gameplay consequences for surgically removed organs.
/// This is intentionally separate from Starlight's optional cyber-organ system,
/// which depends on subsystems that are not present in Frontier.
/// </summary>
public sealed class SurgicalOrganEffectsSystem : EntitySystem
{
    [Dependency] private readonly BlindableSystem _blindable = default!;
    [Dependency] private readonly BodySystem _body = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SolutionContainerSystem _solutions = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;

    private readonly HashSet<EntityUid> _missingLivers = [];
    private readonly HashSet<EntityUid> _missingKidneys = [];
    private readonly HashSet<EntityUid> _missingLungs = [];
    private TimeSpan _nextDamage;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<OrganEyesComponent, SurgeryOrganExtracted>(OnEyesExtracted);
        SubscribeLocalEvent<OrganEyesComponent, SurgeryOrganImplantationCompleted>(OnEyesImplanted);
        SubscribeLocalEvent<OrganTongueComponent, SurgeryOrganExtracted>(OnTongueExtracted);
        SubscribeLocalEvent<OrganTongueComponent, SurgeryOrganImplantationCompleted>(OnTongueImplanted);

        SubscribeLocalEvent<OrganLiverComponent, SurgeryOrganExtracted>(OnLiverExtracted);
        SubscribeLocalEvent<OrganLiverComponent, SurgeryOrganImplantationCompleted>(OnLiverImplanted);
        SubscribeLocalEvent<OrganKidneysComponent, SurgeryOrganExtracted>(OnKidneysExtracted);
        SubscribeLocalEvent<OrganKidneysComponent, SurgeryOrganImplantationCompleted>(OnKidneysImplanted);
        SubscribeLocalEvent<OrganLungsComponent, SurgeryOrganExtracted>(OnLungsExtracted);
        SubscribeLocalEvent<OrganLungsComponent, SurgeryOrganImplantationCompleted>(OnLungsImplanted);
    }

    private void OnEyesExtracted(Entity<OrganEyesComponent> ent, ref SurgeryOrganExtracted args)
    {
        if (!TryComp<BlindableComponent>(args.Body, out var blindable))
            return;

        ent.Comp.EyeDamage = blindable.EyeDamage;
        ent.Comp.MinDamage = blindable.MinDamage;
        Dirty(ent);

        // With the complete eye organ removed, vision stays disabled until a
        // replacement is installed, regardless of temporary healing effects.
        _blindable.SetMinDamage((args.Body, blindable), blindable.MaxDamage);
    }

    private void OnEyesImplanted(Entity<OrganEyesComponent> ent, ref SurgeryOrganImplantationCompleted args)
    {
        if (!TryComp<BlindableComponent>(args.Body, out var blindable))
            return;

        var restoredDamage = ent.Comp.EyeDamage ?? 0;
        _blindable.SetMinDamage((args.Body, blindable), ent.Comp.MinDamage ?? 0);
        _blindable.AdjustEyeDamage((args.Body, blindable), restoredDamage - blindable.EyeDamage);
    }

    private void OnTongueExtracted(Entity<OrganTongueComponent> ent, ref SurgeryOrganExtracted args)
    {
        ent.Comp.IsMuted = HasComp<MutedComponent>(args.Body);
        Dirty(ent);
        EnsureComp<MutedComponent>(args.Body);
    }

    private void OnTongueImplanted(Entity<OrganTongueComponent> ent, ref SurgeryOrganImplantationCompleted args)
    {
        if (!ent.Comp.IsMuted)
            RemComp<MutedComponent>(args.Body);
    }

    private void OnLiverExtracted(Entity<OrganLiverComponent> _, ref SurgeryOrganExtracted args)
        => _missingLivers.Add(args.Body);

    private void OnLiverImplanted(Entity<OrganLiverComponent> _, ref SurgeryOrganImplantationCompleted args)
        => _missingLivers.Remove(args.Body);

    private void OnKidneysExtracted(Entity<OrganKidneysComponent> _, ref SurgeryOrganExtracted args)
        => _missingKidneys.Add(args.Body);

    private void OnKidneysImplanted(Entity<OrganKidneysComponent> _, ref SurgeryOrganImplantationCompleted args)
        => _missingKidneys.Remove(args.Body);

    private void OnLungsExtracted(Entity<OrganLungsComponent> _, ref SurgeryOrganExtracted args)
        => _missingLungs.Add(args.Body);

    private void OnLungsImplanted(Entity<OrganLungsComponent> _, ref SurgeryOrganImplantationCompleted args)
        => _missingLungs.Remove(args.Body);

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        if (_timing.CurTime < _nextDamage)
            return;

        _nextDamage = _timing.CurTime + TimeSpan.FromSeconds(2);

        ApplyMissingOrganDamage<OrganKidneysComponent>(_missingKidneys, "Poison", 1);
        ApplyMissingOrganDamage<OrganLungsComponent>(_missingLungs, "Asphyxiation", 3);

        foreach (var body in _missingLivers.ToArray())
        {
            if (!StillMissing<OrganLiverComponent>(body, _missingLivers)
                || !TryComp<BloodstreamComponent>(body, out var bloodstream)
                || !_solutions.ResolveSolution(body,
                    bloodstream.ChemicalSolutionName,
                    ref bloodstream.ChemicalSolution,
                    out var chemicals))
                continue;

            var containsAlcohol = chemicals.Contents.Any(reagent =>
                reagent.Quantity > FixedPoint2.Zero
                && _prototypes.TryIndex(reagent.Reagent.Prototype, out ReagentPrototype? prototype)
                && prototype.Metabolisms?.ContainsKey("Alcohol") == true);

            if (containsAlcohol)
                ApplyDamage(body, "Poison", 2);
        }
    }

    private void ApplyMissingOrganDamage<T>(HashSet<EntityUid> bodies, string damageType, int amount)
        where T : IComponent
    {
        foreach (var body in bodies.ToArray())
        {
            if (StillMissing<T>(body, bodies))
                ApplyDamage(body, damageType, amount);
        }
    }

    private bool StillMissing<T>(EntityUid body, HashSet<EntityUid> tracked)
        where T : IComponent
    {
        if (TerminatingOrDeleted(body) || !HasComp<BodyComponent>(body))
        {
            tracked.Remove(body);
            return false;
        }

        if (_body.GetBodyOrganEntityComps<T>(body).Count == 0)
            return true;

        tracked.Remove(body);
        return false;
    }

    private void ApplyDamage(EntityUid body, string damageType, int amount)
    {
        if (_mobState.IsDead(body))
            return;

        _damageable.TryChangeDamage(body, new DamageSpecifier
        {
            DamageDict = new Dictionary<string, FixedPoint2>
            {
                [damageType] = amount,
            },
        }, interruptsDoAfters: false);
    }
}

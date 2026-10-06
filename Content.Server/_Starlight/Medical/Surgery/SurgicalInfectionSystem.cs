using System.Linq;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;

namespace Content.Server._Starlight.Medical.Surgery;

/// <summary>Progresses infections on attached living tissue, never on severed parts or paused patients.</summary>
public sealed partial class SurgicalInfectionSystem : EntitySystem
{
    [Dependency] private SurgerySystem _surgery = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MobStateSystem _mobState = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;

    public override void Initialize()
        => SubscribeLocalEvent<SlimeSurgicalInstabilityComponent, RefreshMovementSpeedModifiersEvent>(OnSlimeSpeed);

    private void OnSlimeSpeed(Entity<SlimeSurgicalInstabilityComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
        => args.ModifySpeed(ent.Comp.Speed);

    private bool HasAntibiotic(EntityUid body)
        => TryComp<BloodstreamComponent>(body, out var bloodstream)
            && _solutions.TryGetSolution(body, bloodstream.ChemicalSolutionName, out var solution)
            && solution.Value.Comp.Solution.GetTotalPrototypeQuantity("Surgicillin") >= FixedPoint2.New(0.1);

    private float _elapsed;
    private int _symptomTick;

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < SurgicalInfectionRules.Interval)
            return;
        // No catch-up burst after a long server frame.
        _elapsed = 0;
        var showSymptoms = ++_symptomTick >= 6;
        if (showSymptoms)
            _symptomTick = 0;

        var patients = new Dictionary<EntityUid, float>();
        var medication = new Dictionary<EntityUid, bool>();
        var slimes = new Dictionary<EntityUid, int>();
        var query = EntityQueryEnumerator<SurgicalSterilityComponent>();
        while (query.MoveNext(out var part, out var state))
        {
            var patient = TryComp<BodyPartComponent>(part, out var anatomy) ? anatomy.Body
                : TryComp<OrganComponent>(part, out var organ) ? organ.Body : null;
            if (patient is not { } body || TerminatingOrDeleted(body)
                || MetaData(part).EntityPaused || MetaData(body).EntityPaused
                || _mobState.IsDead(body) || !_surgery.UsesSurgicalSterility(body, part)
                || HasComp<CyberneticOrganComponent>(part)
                || TryComp<SurgicalOrganNecrosisComponent>(part, out var dead) && dead.Dead)
                continue;

            var changed = false;
            var slime = _surgery.IsSurgicalSlime(body);
            if (!medication.TryGetValue(body, out var antibiotic))
                medication[body] = antibiotic = !slime && HasAntibiotic(body);
            foreach (var site in state.Contamination.Keys.Union(state.Infection.Keys).ToArray())
            {
                var previous = state.Infection.GetValueOrDefault(site);
                var contamination = state.Contamination.GetValueOrDefault(site);
                var credit = state.RecoveryCredit.GetValueOrDefault(site);
                var exposure = state.ExposureCredit.GetValueOrDefault(site);
                var infection = SurgicalInfectionRules.Advance(previous, contamination, ref credit, ref exposure, antibiotic);
                state.RecoveryCredit[site] = credit;
                state.ExposureCredit[site] = exposure;
                var cleanup = state.CleanupCredit.GetValueOrDefault(site);
                var remaining = SurgicalInfectionRules.ClearContamination(contamination,
                    _surgery.IsSurgicalSiteOpen(part, site), ref cleanup);
                state.CleanupCredit[site] = cleanup;
                if (remaining != contamination)
                {
                    state.Contamination[site] = remaining;
                    changed = true;
                }
                if (infection == 0 && contamination < SurgicalSterilityRules.ContaminatedSite)
                    changed |= state.Lavaged.Remove(site);
                if (previous != infection)
                {
                    state.Infection[site] = infection;
                    changed = true;
                }

                var amount = SurgicalInfectionRules.Damage(infection);
                if (slime)
                {
                    slimes[body] = Math.Max(slimes.GetValueOrDefault(body), SurgicalInfectionRules.Stage(infection));
                    continue;
                }
                if (amount > 0)
                    patients[body] = SurgicalInfectionRules.CombineDamage(patients.GetValueOrDefault(body), amount);
            }
            if (changed)
                Dirty(part, state);
        }

        var oldEffects = EntityQueryEnumerator<SlimeSurgicalInstabilityComponent>();
        while (oldEffects.MoveNext(out var body, out var effect))
        {
            if (MetaData(body).EntityPaused || slimes.GetValueOrDefault(body) > 0)
                continue;
            effect.Speed = 1f;
            _movement.RefreshMovementSpeedModifiers(body);
            RemCompDeferred<SlimeSurgicalInstabilityComponent>(body);
        }
        foreach (var (body, stage) in slimes)
        {
            if (stage == 0)
                continue;
            var effect = EnsureComp<SlimeSurgicalInstabilityComponent>(body);
            effect.Speed = SurgicalInfectionRules.SlimeSpeed(stage);
            _movement.RefreshMovementSpeedModifiers(body);
            if (showSymptoms)
                _popup.PopupEntity(Loc.GetString("surgical-slime-discomfort"), body, body, PopupType.MediumCaution);
        }

        foreach (var (body, amount) in patients)
        {
            _damage.TryChangeDamage(body, new DamageSpecifier
            {
                DamageDict = new() { ["Poison"] = FixedPoint2.New(amount) },
            }, interruptsDoAfters: false);
            if (showSymptoms)
                _popup.PopupEntity(Loc.GetString("surgical-infection-pain"), body, body, PopupType.MediumCaution);
        }
    }
}

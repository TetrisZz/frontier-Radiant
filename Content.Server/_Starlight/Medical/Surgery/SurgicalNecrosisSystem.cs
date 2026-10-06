using System.Linq;
using Content.Server.Body.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared.Eye.Blinding.Components;
using Content.Shared.Eye.Blinding.Systems;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Robust.Shared.Containers;
using Robust.Shared.Random;

namespace Content.Server._Starlight.Medical.Surgery;

[RegisterComponent]
public sealed partial class SurgicalCardiacWeaknessComponent : Component
{
    public float Speed = 1;
}

public sealed partial class SurgicalNecrosisSystem : EntitySystem
{
    [Dependency] private SurgerySystem _surgery = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private DamageableSystem _damage = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private MetaDataSystem _necrosisMetadata = default!;
    [Dependency] private BlindableSystem _blindable = default!;
    private float _elapsed;
    private int _symptoms;

    public override void Initialize()
        => SubscribeLocalEvent<SurgicalCardiacWeaknessComponent, RefreshMovementSpeedModifiersEvent>(OnSpeed);

    private void OnSpeed(Entity<SurgicalCardiacWeaknessComponent> ent, ref RefreshMovementSpeedModifiersEvent args)
        => args.ModifySpeed(ent.Comp.Speed);

    private bool Active(EntityUid part, EntityUid body)
        => !TerminatingOrDeleted(body) && !MetaData(part).EntityPaused && !MetaData(body).EntityPaused
            && !_mobs.IsDead(body) && _surgery.UsesSurgicalSterility(body, part);

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < SurgicalInfectionRules.Interval)
            return;
        _elapsed = 0;
        var symptoms = ++_symptoms >= 6;
        if (symptoms) _symptoms = 0;
        var detach = new List<(EntityUid Part, EntityUid Body)>();
        var newlyAffected = new HashSet<EntityUid>();
        var parts = EntityQueryEnumerator<SurgicalSterilityComponent, BodyPartComponent>();
        while (parts.MoveNext(out var uid, out var state, out var part))
        {
            // Migrate already detached necrotic limbs from before the explicit dead marker existed.
            if (part.Body == null && _surgery.IsDeadSurgicalItem(uid) && !HasComp<SurgicalDeadLimbComponent>(uid))
            {
                EnsureComp<SurgicalDeadLimbComponent>(uid);
                _necrosisMetadata.SetEntityName(uid,
                    Loc.GetString("surgical-dead-item-name", ("name", MetaData(uid).EntityName)));
            }
            if (part.Body is not { } body || !Active(uid, body) || _surgery.IsSurgicalSlime(body))
                continue;
            foreach (var site in state.Infection.Keys.Union(state.NecrosisSeconds.Keys).ToArray())
            {
                if (!state.NecrosisSeconds.TryGetValue(site, out var age))
                {
                    var severe = SurgicalNecrosisRules.AdvanceSevere(state.SevereSeconds.GetValueOrDefault(site),
                        state.Infection.GetValueOrDefault(site), SurgicalInfectionRules.Interval);
                    state.SevereSeconds[site] = severe;
                    if (severe < SurgicalNecrosisRules.SevereDuration)
                        continue;
                    state.NecrosisSeconds[site] = 0;
                    _popup.PopupEntity(Loc.GetString("surgical-necrosis-onset"), body, body, PopupType.LargeCaution);
                }
                else
                    state.NecrosisSeconds[site] = age += SurgicalInfectionRules.Interval;

                Dirty(uid, state);
                if (part.PartType is BodyPartType.Arm or BodyPartType.Hand or BodyPartType.Leg or BodyPartType.Foot)
                {
                    if (age >= SurgicalNecrosisRules.LimbDeath)
                        detach.Add((uid, body));
                    continue;
                }
                // The superficial torso incision does not infect arbitrary internal organs.
                if (part.PartType != BodyPartType.Torso || site == SurgicalSite.Surface)
                    continue;
                var affected = _body.GetPartOrgans(uid, part).Where(o => MatchesSite(o.Id, site)).ToArray();
                foreach (var organ in affected.Where(o => HasComp<OrganHeartComponent>(o.Id)))
                {
                    if (!HasComp<SurgicalOrganNecrosisComponent>(organ.Id))
                    {
                        EnsureComp<SurgicalOrganNecrosisComponent>(organ.Id);
                        newlyAffected.Add(organ.Id);
                    }
                }
                // One non-cardiac organ at a time; dead organs remain for extraction.
                var next = affected.FirstOrDefault(o => !HasComp<OrganHeartComponent>(o.Id)
                    && !(TryComp<SurgicalOrganNecrosisComponent>(o.Id, out var old) && old.Dead));
                if (next.Id != default && !HasComp<SurgicalOrganNecrosisComponent>(next.Id))
                {
                    EnsureComp<SurgicalOrganNecrosisComponent>(next.Id);
                    newlyAffected.Add(next.Id);
                }
            }
        }

        // Detach outside the anatomy query using the same container path as amputation.
        foreach (var (part, body) in detach.Distinct())
        {
            if (TryComp<BodyPartComponent>(part, out var anatomy) && anatomy.Body == body
                && _containers.TryGetContainingContainer((part, null, null), out var container))
            {
                foreach (var child in _body.GetBodyPartChildren(part).ToArray())
                {
                    if (HasComp<SurgicalDeadLimbComponent>(child.Id) || !_surgery.UsesSurgicalSterility(body, child.Id))
                        continue;
                    EnsureComp<SurgicalDeadLimbComponent>(child.Id);
                    _necrosisMetadata.SetEntityName(child.Id,
                        Loc.GetString("surgical-dead-item-name", ("name", MetaData(child.Id).EntityName)));
                }
                _containers.Remove(part, container, force: true, destination: Transform(body).Coordinates);
            }
        }

        var cardiac = new Dictionary<EntityUid, float>();
        var infectedOrgans = EntityQueryEnumerator<SurgicalSterilityComponent, OrganComponent>();
        while (infectedOrgans.MoveNext(out var uid, out var infection, out var organ))
        {
            if (organ.Body is not { } body || !Active(uid, body) || _surgery.IsSurgicalSlime(body)
                || HasComp<CyberneticOrganComponent>(uid) || HasComp<SurgicalOrganNecrosisComponent>(uid))
                continue;
            var severe = SurgicalNecrosisRules.AdvanceSevere(
                infection.SevereSeconds.GetValueOrDefault(SurgicalSite.Surface),
                infection.Infection.GetValueOrDefault(SurgicalSite.Surface), SurgicalInfectionRules.Interval);
            infection.SevereSeconds[SurgicalSite.Surface] = severe;
            if (severe < SurgicalNecrosisRules.SevereDuration)
                continue;
            EnsureComp<SurgicalOrganNecrosisComponent>(uid);
            newlyAffected.Add(uid);
            _popup.PopupEntity(Loc.GetString("surgical-necrosis-onset"), body, body, PopupType.LargeCaution);
        }
        var organs = EntityQueryEnumerator<SurgicalOrganNecrosisComponent, OrganComponent>();
        while (organs.MoveNext(out var uid, out var necrosis, out var organ))
        {
            if (necrosis.Dead && !necrosis.NamedAsDead)
                NameDeadOrgan(uid, necrosis);
            if (organ.Body is not { } body || !Active(uid, body))
                continue;
            var heart = HasComp<OrganHeartComponent>(uid);
            if (!necrosis.Dead && !newlyAffected.Contains(uid))
            {
                necrosis.Seconds += SurgicalInfectionRules.Interval;
                necrosis.Dead = necrosis.Seconds >= (heart ? SurgicalNecrosisRules.HeartDeath : SurgicalNecrosisRules.OrganDeath);
                Dirty(uid, necrosis);
                if (necrosis.Dead)
                {
                    NameDeadOrgan(uid, necrosis);
                    // Physiology remains disabled even if the dead organ is transplanted.
                    RemCompDeferred<MetabolizerComponent>(uid);
                    RemCompDeferred<LungComponent>(uid);
                    RemCompDeferred<StomachComponent>(uid);
                }
            }
            if (heart)
            {
                var function = SurgicalNecrosisRules.HeartFunction(necrosis.Seconds);
                cardiac[body] = Math.Min(cardiac.GetValueOrDefault(body, 1f), 0.5f + 0.5f * function);
                var arrhythmia = _random.Prob(1f - function);
                if (arrhythmia || necrosis.Dead)
                    Hurt(body, "Asphyxiation", necrosis.Dead ? 10 : 1 + 3 * (1 - function));
                if (symptoms)
                    _popup.PopupEntity(Loc.GetString(necrosis.Dead ? "surgical-heart-dead" : "surgical-heart-arrhythmia"),
                        body, body, PopupType.LargeCaution);
            }
            else if (necrosis.Dead)
            {
                if (HasComp<OrganEyesComponent>(uid) && TryComp<BlindableComponent>(body, out var blindable))
                    _blindable.SetMinDamage((body, blindable), blindable.MaxDamage);
                Hurt(body, HasComp<OrganLungsComponent>(uid) ? "Asphyxiation" : "Poison", 2);
            }
        }
        var oldEffects = EntityQueryEnumerator<SurgicalCardiacWeaknessComponent>();
        while (oldEffects.MoveNext(out var uid, out var effect))
        {
            if (MetaData(uid).EntityPaused || cardiac.ContainsKey(uid))
                continue;
            effect.Speed = 1;
            _movement.RefreshMovementSpeedModifiers(uid);
            RemCompDeferred<SurgicalCardiacWeaknessComponent>(uid);
        }
        foreach (var (body, speed) in cardiac)
        {
            EnsureComp<SurgicalCardiacWeaknessComponent>(body).Speed = speed;
            _movement.RefreshMovementSpeedModifiers(body);
        }
    }

    private void NameDeadOrgan(EntityUid uid, SurgicalOrganNecrosisComponent necrosis)
    {
        _necrosisMetadata.SetEntityName(uid,
            Loc.GetString("surgical-dead-item-name", ("name", MetaData(uid).EntityName)));
        necrosis.NamedAsDead = true;
        Dirty(uid, necrosis);
    }

    public void Debride(EntityUid part, SurgicalSite site)
    {
        if (TryComp<SurgicalSterilityComponent>(part, out var state))
        {
            state.NecrosisSeconds.Remove(site);
            state.SevereSeconds.Remove(site);
            Dirty(part, state);
        }
        foreach (var organ in _body.GetPartOrgans(part).ToArray())
        {
            if (MatchesSite(organ.Id, site) && !HasComp<OrganHeartComponent>(organ.Id)
                && TryComp<SurgicalOrganNecrosisComponent>(organ.Id, out var necrosis) && !necrosis.Dead)
                RemComp<SurgicalOrganNecrosisComponent>(organ.Id);
        }
    }

    public void RestoreHeart(EntityUid part)
    {
        foreach (var organ in _body.GetPartOrgans(part).ToArray())
        {
            if (HasComp<OrganHeartComponent>(organ.Id)
                && TryComp<SurgicalOrganNecrosisComponent>(organ.Id, out var necrosis) && !necrosis.Dead)
                RemComp<SurgicalOrganNecrosisComponent>(organ.Id);
        }
    }

    private bool MatchesSite(EntityUid organ, SurgicalSite site)
    {
        if (HasComp<CyberneticOrganComponent>(organ)
            || TryComp<FunctionalOrganComponent>(organ, out var functional) && functional.IsCybernetic)
            return false;
        return site switch
        {
            SurgicalSite.Ribcage => HasComp<OrganHeartComponent>(organ) || HasComp<OrganLungsComponent>(organ),
            SurgicalSite.Abdomen => HasComp<OrganLiverComponent>(organ) || HasComp<OrganKidneysComponent>(organ)
                || HasComp<OrganStomachComponent>(organ) || HasComp<OrganAppendixComponent>(organ),
            _ => false,
        };
    }

    private void Hurt(EntityUid body, string type, float amount)
        => _damage.TryChangeDamage(body, new DamageSpecifier
        {
            DamageDict = new() { [type] = FixedPoint2.New(amount) },
        }, interruptsDoAfters: false);
}

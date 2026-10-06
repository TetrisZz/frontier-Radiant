using System.Linq;
using Content.Server.Emp;
using Content.Server._Starlight.Medical.Surgery.Components;
using Content.Shared.Damage;
using Content.Shared.Electrocution;
using Content.Shared.Emp;
using Content.Shared.Movement.Events;
using Content.Shared.Movement.Systems;
using Content.Shared.Popups;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server._Starlight.Medical.Surgery;

/// <summary>
/// Runs the two spawn-only heart treatment implants.  The device itself is
/// EMP-disabled, while its implanted state is managed by the normal organ
/// surgery events.
/// </summary>
public sealed partial class ChestMedicalImplantSystem : EntitySystem
{
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedElectrocutionSystem _electrocution = default!;
    [Dependency] private IGameTiming _timing = default!;
    [Dependency] private MovementSpeedModifierSystem _movement = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IRobustRandom _random = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ChestMedicalImplantComponent, SurgeryOrganImplantationCompleted>(OnInstalled);
        SubscribeLocalEvent<ChestMedicalImplantComponent, SurgeryOrganExtracted>(OnExtracted);
        SubscribeLocalEvent<ChestMedicalImplantComponent, EmpPulseEvent>(OnEmpPulse);
        SubscribeLocalEvent<ChestMedicalImplantComponent, EmpDisabledRemoved>(OnEmpDisabledRemoved);
        SubscribeLocalEvent<HeartImplantEmpEffectComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnInstalled(Entity<ChestMedicalImplantComponent> ent, ref SurgeryOrganImplantationCompleted args)
    {
        ent.Comp.Body = args.Body;
        ent.Comp.EmpDisabled = false;
        ent.Comp.NextHealing = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.Interval);
        Dirty(ent);
    }

    private void OnExtracted(Entity<ChestMedicalImplantComponent> ent, ref SurgeryOrganExtracted args)
    {
        RemoveEmpEffect(ent.Owner, ent.Comp.Body);
        ent.Comp.Body = null;
        ent.Comp.EmpDisabled = false;
        Dirty(ent);
    }

    private void OnEmpPulse(Entity<ChestMedicalImplantComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        args.Disabled = true;
        ent.Comp.EmpDisabled = true;
        if (ent.Comp.Body is { } body)
        {
            var effect = EnsureComp<HeartImplantEmpEffectComponent>(body);
            if (effect.Sources.Add(ent.Owner) && effect.Sources.Count == 1)
            {
                effect.NextTick = _timing.CurTime + TimeSpan.FromSeconds(1);
                effect.NextPainPopup = _timing.CurTime;
                _movement.RefreshMovementSpeedModifiers(body);
            }
        }
        Dirty(ent);
    }

    private void OnEmpDisabledRemoved(Entity<ChestMedicalImplantComponent> ent, ref EmpDisabledRemoved args)
    {
        RemoveEmpEffect(ent.Owner, ent.Comp.Body);
        ent.Comp.EmpDisabled = false;
        ent.Comp.NextHealing = _timing.CurTime + TimeSpan.FromSeconds(ent.Comp.Interval);
        Dirty(ent);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<ChestMedicalImplantComponent>();
        while (query.MoveNext(out _, out var implant))
        {
            if (implant.Body is not { } body ||
                implant.EmpDisabled ||
                TerminatingOrDeleted(body) ||
                _timing.CurTime < implant.NextHealing)
                continue;

            implant.NextHealing = _timing.CurTime + TimeSpan.FromSeconds(implant.Interval);
            _damageable.TryChangeDamage(body, implant.Healing, interruptsDoAfters: false);
        }

        var effects = EntityQueryEnumerator<HeartImplantEmpEffectComponent>();
        while (effects.MoveNext(out var body, out var effect))
        {
            effect.Sources.RemoveWhere(uid =>
                TerminatingOrDeleted(uid)
                || !TryComp<ChestMedicalImplantComponent>(uid, out var implant)
                || !implant.EmpDisabled
                || implant.Body != body);

            if (effect.Sources.Count == 0)
            {
                RemCompDeferred<HeartImplantEmpEffectComponent>(body);
                _movement.RefreshMovementSpeedModifiers(body);
                continue;
            }

            if (_timing.CurTime >= effect.NextPainPopup)
            {
                effect.NextPainPopup = _timing.CurTime + TimeSpan.FromSeconds(5);
                _popup.PopupEntity(Loc.GetString("starlight-heart-implant-emp-pain"), body, body,
                    PopupType.LargeCaution);
            }

            if (_timing.CurTime < effect.NextTick)
                continue;

            effect.NextTick = _timing.CurTime + TimeSpan.FromSeconds(1);

            // A 20% tick chance sits in the requested 10–25% range and mirrors
            // Licoxide/Tazinide by using the normal electrocution system.
            if (_random.Prob(0.20f))
                _electrocution.TryDoElectrocution(body, effect.Sources.First(), 5,
                    TimeSpan.FromSeconds(2), refresh: true, ignoreInsulation: true);
        }
    }

    private void OnRefreshSpeed(Entity<HeartImplantEmpEffectComponent> ent,
        ref RefreshMovementSpeedModifiersEvent args)
    {
        if (ent.Comp.Sources.Count > 0)
            args.ModifySpeed(0.8f);
    }

    private void RemoveEmpEffect(EntityUid implant, EntityUid? body)
    {
        if (body is not { } bodyUid || !TryComp<HeartImplantEmpEffectComponent>(bodyUid, out var effect))
            return;

        effect.Sources.Remove(implant);
        if (effect.Sources.Count != 0)
            return;

        RemComp<HeartImplantEmpEffectComponent>(bodyUid);
        _movement.RefreshMovementSpeedModifiers(bodyUid);
    }
}

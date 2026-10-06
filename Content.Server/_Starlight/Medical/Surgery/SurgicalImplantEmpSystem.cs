using Content.Server.Emp;
using Content.Server._Starlight.Medical.Surgery.Components;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared.Body.Components;
using Content.Shared.Body.Systems;
using Content.Shared.Emp;
using Content.Shared.Implants.Components;
using Content.Shared._Starlight.Medical.Limbs;
using Robust.Shared.Containers;
using Robust.Shared.Serialization.Manager;

namespace Content.Server._Starlight.Medical.Surgery;

/// <summary>
/// An EMP affects electronics implanted inside a patient as well as devices worn on them.
/// Organs are container children, so they are not otherwise found by a spatial EMP pulse.
/// </summary>
public sealed partial class SurgicalImplantEmpSystem : EntitySystem
{
    [Dependency] private EmpSystem _emp = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private DamageableSystem _damageable = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private ISerializationManager _serialization = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BodyComponent, EmpPulseEvent>(OnBodyEmpPulse);
        SubscribeLocalEvent<SubdermalImplantComponent, EmpPulseEvent>(OnSubdermalEmpPulse);
        SubscribeLocalEvent<SubdermalImplantComponent, EmpDisabledRemoved>(OnSubdermalEmpRemoved);
        SubscribeLocalEvent<CyberneticOrganComponent, EmpPulseEvent>(OnCyberneticOrganEmpPulse);
        SubscribeLocalEvent<CyberneticOrganComponent, EmpDisabledRemoved>(OnCyberneticOrganEmpRemoved);
    }

    private void OnCyberneticOrganEmpPulse(Entity<CyberneticOrganComponent> ent, ref EmpPulseEvent args)
    {
        args.Affected = true;
        args.Disabled = true;

        var state = EnsureComp<EmpDisabledCyberneticOrganComponent>(ent);
        if (state.Removed.Count > 0)
            return;

        foreach (var registration in ent.Comp.DisableComponents.Values)
        {
            var type = registration.Component.GetType();
            if (!EntityManager.TryGetComponent(ent.Owner, type, out var component))
                continue;

            state.Removed[type] = _serialization.CreateCopy(component, notNullableOverride: true);
            RemComp(ent.Owner, component);
        }
    }

    private void OnCyberneticOrganEmpRemoved(Entity<CyberneticOrganComponent> ent, ref EmpDisabledRemoved args)
    {
        if (!TryComp<EmpDisabledCyberneticOrganComponent>(ent, out var state))
            return;

        foreach (var component in state.Removed.Values)
            if (!HasComp(ent.Owner, component.GetType()))
                AddComp(ent.Owner, component);

        RemComp<EmpDisabledCyberneticOrganComponent>(ent);
    }

    private void OnSubdermalEmpPulse(Entity<SubdermalImplantComponent> ent, ref EmpPulseEvent args)
    {
        if (ent.Comp.ImplantedEntity is not { } body)
            return;

        args.Affected = true;
        args.Disabled = true;

        var state = EnsureComp<EmpDisabledSubdermalImplantComponent>(ent);
        if (state.Disabled)
            return;

        state.Disabled = true;
        EntityManager.RemoveComponents(body, ent.Comp.ImplantComponents);
        _actions.RemoveAction(body, ent.Comp.Action);
        ent.Comp.Action = null;
        Dirty(ent);
    }

    private void OnSubdermalEmpRemoved(Entity<SubdermalImplantComponent> ent, ref EmpDisabledRemoved args)
    {
        if (!TryComp<EmpDisabledSubdermalImplantComponent>(ent, out var state)
            || !state.Disabled
            || ent.Comp.ImplantedEntity is not { } body
            || TerminatingOrDeleted(body))
            return;

        EntityManager.AddComponents(body, ent.Comp.ImplantComponents);
        if (ent.Comp.ImplantAction is { } action)
            _actions.AddAction(body, ref ent.Comp.Action, action, ent.Owner);

        RemComp<EmpDisabledSubdermalImplantComponent>(ent);
        Dirty(ent);
    }

    private void OnBodyEmpPulse(Entity<BodyComponent> ent, ref EmpPulseEvent args)
    {
        var electronicCount = 0;
        foreach (var (organ, _) in _body.GetBodyOrgans(ent.Owner, ent.Comp))
        {
            if (!IsElectronicOrgan(organ))
                continue;

            electronicCount++;
            _emp.DoEmpEffects(organ, args.EnergyConsumption, (float) args.Duration.TotalSeconds);
        }

        // Body parts are container children too. Forward the pulse to every
        // Starlight cyberpart, including hands nested inside preassembled arms.
        foreach (var (part, _) in _body.GetBodyChildren(ent.Owner, ent.Comp))
        {
            if (!HasComp<ReversibleCyberLimbComponent>(part))
                continue;

            if (IsCyberLimbRoot(part))
                electronicCount++;
            _emp.DoEmpEffects(part, args.EnergyConsumption, (float) args.Duration.TotalSeconds);
        }

        if (TryComp<ImplantedComponent>(ent, out var implanted))
        {
            foreach (var implant in implanted.ImplantContainer.ContainedEntities)
            {
                electronicCount++;
                _emp.DoEmpEffects(implant, args.EnergyConsumption, (float) args.Duration.TotalSeconds);
            }
        }

        if (electronicCount > 0)
        {
            _damageable.TryChangeDamage(ent.Owner, new DamageSpecifier
            {
                DamageDict = new Dictionary<string, FixedPoint2> { ["Shock"] = 5 * electronicCount },
            }, interruptsDoAfters: false);
        }
    }

    private bool IsCyberLimbRoot(EntityUid part)
    {
        return !_containers.TryGetContainingContainer((part, null, null), out var container)
               || !HasComp<ReversibleCyberLimbComponent>(container.Owner);
    }

    private bool IsElectronicOrgan(EntityUid organ)
    {
        return HasComp<CyberneticOrganComponent>(organ)
               || TryComp<FunctionalOrganComponent>(organ, out var functional) && functional.IsCybernetic
               || HasComp<ActionOrganComponent>(organ)
               || HasComp<StorageOrganComponent>(organ)
               || HasComp<ChestMedicalImplantComponent>(organ);
    }
}

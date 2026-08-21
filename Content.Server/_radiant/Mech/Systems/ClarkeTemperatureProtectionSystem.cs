using Content.Server._radiant.Mech.Components;
using Content.Server.Temperature.Components;
using Content.Server.Temperature.Systems;
using Content.Shared.Atmos;
using Content.Shared.Mech.Components;
using Robust.Shared.Containers;

namespace Content.Server._radiant.Mech.Systems;

/// <summary>
/// Applies Clarke's thermal insulation directly to the pilot. This remains reliable for occupants
/// held in a container, where parent threshold propagation can otherwise be delayed.
/// </summary>
public sealed class ClarkeTemperatureProtectionSystem : EntitySystem
{
    [Dependency] private readonly TemperatureSystem _temperatureSystem = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ClarkeTemperatureProtectionComponent, EntInsertedIntoContainerMessage>(OnPilotInserted);
        SubscribeLocalEvent<ClarkeTemperatureProtectionComponent, EntRemovedFromContainerMessage>(OnPilotRemoved);
        SubscribeLocalEvent<ClarkePilotTemperatureProtectionComponent, GetTemperatureProtectionEvent>(OnTemperatureProtection);
    }

    private void OnPilotInserted(EntityUid uid, ClarkeTemperatureProtectionComponent component,
        EntInsertedIntoContainerMessage args)
    {
        if (!TryComp<MechComponent>(uid, out var mech) || args.Container != mech.PilotSlot ||
            !TryComp<TemperatureComponent>(args.Entity, out var temperature))
        {
            return;
        }

        var protection = EnsureComp<ClarkePilotTemperatureProtectionComponent>(args.Entity);
        protection.HeatDamageThreshold = temperature.HeatDamageThreshold;
        protection.ColdDamageThreshold = temperature.ColdDamageThreshold;
        protection.ParentHeatDamageThreshold = temperature.ParentHeatDamageThreshold;
        protection.ParentColdDamageThreshold = temperature.ParentColdDamageThreshold;

        temperature.HeatDamageThreshold = component.HeatDamageThreshold;
        temperature.ColdDamageThreshold = component.ColdDamageThreshold;
        temperature.ParentHeatDamageThreshold = component.HeatDamageThreshold;
        temperature.ParentColdDamageThreshold = component.ColdDamageThreshold;
        Dirty(args.Entity, temperature);

        // Stop further heat transfer while inside and reset any temperature damage accumulated outside.
        protection.AddedTemperatureProtection = !HasComp<TemperatureProtectionComponent>(args.Entity);
        EnsureComp<TemperatureProtectionComponent>(args.Entity);
        _temperatureSystem.ForceChangeTemperature(args.Entity, Atmospherics.T20C, temperature);
    }

    private void OnPilotRemoved(EntityUid uid, ClarkeTemperatureProtectionComponent component,
        EntRemovedFromContainerMessage args)
    {
        if (!TryComp<MechComponent>(uid, out var mech) || args.Container != mech.PilotSlot ||
            !TryComp<TemperatureComponent>(args.Entity, out var temperature) ||
            !TryComp<ClarkePilotTemperatureProtectionComponent>(args.Entity, out var protection))
        {
            return;
        }

        temperature.HeatDamageThreshold = protection.HeatDamageThreshold;
        temperature.ColdDamageThreshold = protection.ColdDamageThreshold;
        temperature.ParentHeatDamageThreshold = protection.ParentHeatDamageThreshold;
        temperature.ParentColdDamageThreshold = protection.ParentColdDamageThreshold;
        Dirty(args.Entity, temperature);

        if (protection.AddedTemperatureProtection)
            RemComp<TemperatureProtectionComponent>(args.Entity);
        RemComp<ClarkePilotTemperatureProtectionComponent>(args.Entity);
    }

    private void OnTemperatureProtection(Entity<ClarkePilotTemperatureProtectionComponent> pilot,
        ref GetTemperatureProtectionEvent args)
    {
        args.Coefficient = 0f;
    }
}

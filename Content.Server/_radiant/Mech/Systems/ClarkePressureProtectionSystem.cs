using Content.Server._radiant.Mech.Components;
using Content.Server.Atmos.Components;
using Content.Shared.Mech.Components;
using Robust.Shared.Containers;

namespace Content.Server._radiant.Mech.Systems;

/// <summary>
/// Lets Clarke's cockpit block barotrauma while intentionally leaving the pilot exposed to the local air.
/// </summary>
public sealed class ClarkePressureProtectionSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<ClarkePressureProtectionComponent, EntInsertedIntoContainerMessage>(OnPilotInserted);
        SubscribeLocalEvent<ClarkePressureProtectionComponent, EntRemovedFromContainerMessage>(OnPilotRemoved);
    }

    private void OnPilotInserted(EntityUid uid, ClarkePressureProtectionComponent component,
        EntInsertedIntoContainerMessage args)
    {
        if (!TryComp<MechComponent>(uid, out var mech) || args.Container != mech.PilotSlot)
            return;

        var pilotProtection = EnsureComp<ClarkePilotPressureProtectionComponent>(args.Entity);
        if (HasComp<PressureImmunityComponent>(args.Entity))
            return;

        EnsureComp<PressureImmunityComponent>(args.Entity);
        pilotProtection.AddedPressureImmunity = true;
    }

    private void OnPilotRemoved(EntityUid uid, ClarkePressureProtectionComponent component,
        EntRemovedFromContainerMessage args)
    {
        if (!TryComp<MechComponent>(uid, out var mech) || args.Container != mech.PilotSlot ||
            !TryComp<ClarkePilotPressureProtectionComponent>(args.Entity, out var pilotProtection))
        {
            return;
        }

        if (pilotProtection.AddedPressureImmunity)
            RemComp<PressureImmunityComponent>(args.Entity);

        RemComp<ClarkePilotPressureProtectionComponent>(args.Entity);
    }
}

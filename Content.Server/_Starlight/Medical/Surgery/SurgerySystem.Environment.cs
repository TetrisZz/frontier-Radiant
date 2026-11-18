using Content.Server.Power.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Fluids.Components;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;

namespace Content.Server._Starlight.Medical.Surgery;

public sealed partial class SurgerySystem
{
    [Dependency] private EntityLookupSystem _sterilityLookup = default!;

    private int EnvironmentRisk(EntityUid patient)
    {
        if (!TryComp<BuckleComponent>(patient, out var buckle)
            || buckle.BuckledTo is not { } table || !HasComp<OperatingTableComponent>(table))
            return 0;

        var puddles = 0;
        var sterilizer = false;
        // Only exposed puddles and sterilizers affect the operating environment.
        foreach (var uid in _sterilityLookup.GetEntitiesInRange(table, 9f, LookupFlags.Uncontained))
        {
            if (TerminatingOrDeleted(uid))
                continue;
            if (HasComp<PuddleComponent>(uid))
            {
                if (IsSurgicalPuddleHazard(uid))
                    puddles++;
            }
            if (HasComp<SurgicalSterilizerComponent>(uid) && Transform(uid).Anchored
                && TryComp<ApcPowerReceiverComponent>(uid, out var power) && power.Powered)
                sterilizer = true;
        }
        return SurgicalSterilityRules.EnvironmentRisk(puddles, sterilizer);
    }

    public bool IsSurgicalPuddleHazard(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid) || EntityManager.IsQueuedForDeletion(uid)
            || !TryComp<PuddleComponent>(uid, out var puddle))
            return false;
        var puddleSolution = puddle.Solution;
        if (!_sterilitySolutions.ResolveSolution(uid, puddle.SolutionName, ref puddleSolution, out var liquid)
            || liquid.Volume <= 0)
            return false;

        var drains = EntityQueryEnumerator<DrainComponent>();
        while (drains.MoveNext(out var drainId, out var drain))
        {
            if (TerminatingOrDeleted(drainId) || EntityManager.IsQueuedForDeletion(drainId)
                || MetaData(drainId).EntityPaused || !drain.AutoDrain || drain.UnitsPerSecond <= 0)
                continue;
            var buffer = drain.Solution;
            if (!_sterilitySolutions.ResolveSolution(drainId, DrainComponent.SolutionName, ref buffer, out var solution)
                || solution.AvailableVolume <= 0)
                continue;
            // Match the actual drain system's area, including drains just outside the operating room radius.
            if (_sterilityLookup.GetEntitiesInRange(Transform(drainId).Coordinates, drain.Range).Contains(uid))
                return false;
        }
        return true;
    }
}

using Content.Server.Power.Components;
using Content.Shared.Buckle.Components;
using Content.Shared.Fluids.Components;
using Content.Shared.Tag;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;

namespace Content.Server._Starlight.Medical.Surgery;

public sealed partial class SurgerySystem
{
    [Dependency] private EntityLookupSystem _sterilityLookup = default!;
    [Dependency] private TagSystem _sterilityTags = default!;

    private int EnvironmentRisk(EntityUid patient)
    {
        if (!TryComp<BuckleComponent>(patient, out var buckle)
            || buckle.BuckledTo is not { } table || !HasComp<OperatingTableComponent>(table))
            return 0;

        var trash = 0;
        var puddles = 0;
        var sterilizer = false;
        // Only exposed objects, not rubbish stored in bags or closed containers.
        foreach (var uid in _sterilityLookup.GetEntitiesInRange(table, 9f, LookupFlags.Uncontained))
        {
            if (TerminatingOrDeleted(uid))
                continue;
            if (HasComp<PuddleComponent>(uid))
                puddles++;
            else if (_sterilityTags.HasTag(uid, "Trash"))
                trash++;
            if (HasComp<SurgicalSterilizerComponent>(uid) && Transform(uid).Anchored
                && TryComp<ApcPowerReceiverComponent>(uid, out var power) && power.Powered)
                sterilizer = true;
        }
        return SurgicalSterilityRules.EnvironmentRisk(trash, puddles, sterilizer);
    }
}

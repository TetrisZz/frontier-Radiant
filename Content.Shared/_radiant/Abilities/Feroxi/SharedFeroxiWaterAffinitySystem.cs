using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Content.Shared.Slippery;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Abilities.Feroxi;

/// <summary>
/// Handles the predicted portion of the feroxi water affinity on both client and server.
/// </summary>
public sealed class SharedFeroxiWaterAffinitySystem : EntitySystem
{
    private static readonly ProtoId<ReagentPrototype> WaterReagent = "Water";

    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<FeroxiWaterAffinityComponent, SlipAttemptEvent>(OnSlipAttempt);
    }

    private void OnSlipAttempt(Entity<FeroxiWaterAffinityComponent> entity, ref SlipAttemptEvent args)
    {
        if (args.SlipCausingEntity is { } cause && IsWaterPuddle(cause))
            args.NoSlip = true;
    }

    private bool IsWaterPuddle(EntityUid puddleUid)
    {
        if (!TryComp(puddleUid, out PuddleComponent? puddle) ||
            !_solutions.ResolveSolution(puddleUid, puddle.SolutionName, ref puddle.Solution, out var solution))
        {
            return false;
        }

        return solution.GetTotalPrototypeQuantity(WaterReagent) > FixedPoint2.Zero;
    }
}

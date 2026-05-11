using Content.Shared._radiant.Arousal;
using Content.Shared._radiant.Arousal.Components;
using Content.Shared.Chemistry.Reagent;
using Robust.Shared.Prototypes;

namespace Content.Shared.EntityEffects.Effects;

/// <summary>
/// Adds arousal on metabolism; <see cref="Amount"/> is scaled by metabolism <c>Scale</c> like drunk/booze effects.
/// </summary>
public sealed partial class ModifyArousal : EntityEffect
{
    /// <summary>
    /// Arousal added per full metabolism tick (before scale).
    /// </summary>
    [DataField]
    public float Amount = 1f;

    public override void Effect(EntityEffectBaseArgs args)
    {
        var amount = Amount;
        if (args is EntityEffectReagentArgs reagentArgs)
            amount *= reagentArgs.Scale.Float();

        if (amount <= 0f)
            return;

        args.EntityManager.EnsureComponent<ArousalComponent>(args.TargetEntity);
        var ev = new ApplyArousalFromMetabolismEvent(amount);
        args.EntityManager.EventBus.RaiseLocalEvent(args.TargetEntity, ref ev);
    }

    protected override string? ReagentEffectGuidebookText(IPrototypeManager prototype, IEntitySystemManager entSys) => null;
}

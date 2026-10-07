using System.Linq;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Reaction;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;

namespace Content.Shared._radiant.Skills;

public sealed partial class SharedProfessionalSkillsSystem
{
    public bool CanTransferSolutions(EntityUid user, EntityUid targetEntity, Solution source, Solution target, FixedPoint2 amount)
    {
        if (amount <= 0)
            return true;

        if (Requirement(targetEntity, SkillAction.Pour) is { } planting)
        {
            var mutagen = source.Contents.Any(r => r.Reagent.Prototype is "UnstableMutagen" or "Left4Zed");
            var fertilizer = source.Contents.Any(r => r.Reagent.Prototype != "Water");
            if (!Check(user, ProfessionalSkill.Botany, mutagen ? 3 : fertilizer ? planting.Level : 0))
                return false;
        }

        if (Level(user, ProfessionalSkill.Cooking) >= 3 || source.Volume <= 0)
            return true;
        var mixed = new Dictionary<string, FixedPoint2>();
        foreach (var (reagent, quantity) in target.Contents)
            mixed[reagent.Prototype] = mixed.GetValueOrDefault(reagent.Prototype) + quantity;
        foreach (var (reagent, quantity) in source.Contents)
            mixed[reagent.Prototype] = mixed.GetValueOrDefault(reagent.Prototype) + quantity * amount / source.Volume;

        var sourceCapacity = source.GetHeatCapacity(_prototypes) * (float) (amount / source.Volume);
        var targetCapacity = target.GetHeatCapacity(_prototypes);
        var capacity = sourceCapacity + targetCapacity;
        var temperature = capacity > 0
            ? (sourceCapacity * source.Temperature + targetCapacity * target.Temperature) / capacity
            : target.Temperature;

        // Reject the transfer before consuming liquid if it would create a drink beyond the user's skill.
        // Natural/environmental chemical reactions are not disabled.
        foreach (var reaction in _prototypes.EnumeratePrototypes<ReactionPrototype>())
        {
            if (reaction.Products.Count == 0 || reaction.Reactants.Count == 0
                || temperature < reaction.MinimumTemperature || temperature > reaction.MaximumTemperature
                || reaction.MixingCategories is { Count: > 0 }
                || !reaction.Reactants.Keys.Any(id => source.GetTotalPrototypeQuantity(id) > 0)
                || !reaction.Products.Keys.Any(id => _prototypes.TryIndex<ReagentPrototype>(id, out var reagent) && reagent.Group == "Drinks")
                || !reaction.Reactants.All(r => mixed.GetValueOrDefault(r.Key) >= r.Value.Amount))
                continue;
            var simple = reaction.Products.Keys.Any(id => id is "Coffee" or "Tea" or "GreenTea" or "HotCocoa");
            var required = simple || reaction.Reactants.Count <= 1 ? 1 : reaction.Reactants.Count <= 3 ? 2 : 3;
            if (!Check(user, ProfessionalSkill.Cooking, required))
                return false;
        }
        return true;
    }
}

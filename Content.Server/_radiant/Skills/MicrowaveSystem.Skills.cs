using System.Linq;
using Content.Server.Kitchen.Components;
using Content.Shared._radiant.Skills;
using Content.Shared.FixedPoint;
using Content.Shared.Kitchen;
using Content.Shared.Stacks;

namespace Content.Server.Kitchen.EntitySystems;

public sealed partial class MicrowaveSystem
{
    private bool CanCookContents(EntityUid uid, MicrowaveComponent microwave, EntityUid user)
    {
        var skills = EntityManager.System<SharedProfessionalSkillsSystem>();
        if (skills.Level(user, ProfessionalSkill.Cooking) >= 3)
            return true;
        var solids = new Dictionary<string, int>();
        var reagents = new Dictionary<string, FixedPoint2>();
        // Read-only preflight, before microwaving events can consume or alter any ingredient.
        foreach (var item in microwave.Storage.ContainedEntities)
        {
            string? id = TryComp<StackComponent>(item, out var stack)
                ? _prototype.Index<StackPrototype>(stack.StackTypeId).Spawn.Id : MetaData(item).EntityPrototype?.ID;
            if (id == null)
                continue;
            solids[id] = solids.GetValueOrDefault(id) + (stack?.Count ?? 1);
            if (!_solutionContainer.TryGetDrainableSolution(item, out _, out var solution))
                continue;
            foreach (var (reagent, quantity) in solution.Contents)
                reagents[reagent.Prototype] = reagents.GetValueOrDefault(reagent.Prototype) + quantity;
        }
        var secret = new GetSecretRecipesEvent();
        RaiseLocalEvent(uid, ref secret);
        // A batch can satisfy several recipes. A simple recipe must not hide a harder one.
        foreach (var recipe in secret.Recipes.Concat(_recipeManager.Recipes))
            if (CanSatisfyRecipe(microwave, recipe, solids, reagents).Item2 > 0
                && !skills.Check(user, ProfessionalSkill.Cooking, recipe.RequiredCookingLevel))
                return false;
        return skills.Check(user, ProfessionalSkill.Cooking, 1);
    }
}

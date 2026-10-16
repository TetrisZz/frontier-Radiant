using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.ResearchBlueprints;

public abstract class SharedBlueprintResearchSystem : EntitySystem
{
    public void SetBlueprint(Entity<BlueprintComponent> ent, HashSet<ProtoId<LatheRecipePrototype>> recipes, BlueprintModifiers modifiers)
    {
        ent.Comp.ProvidedRecipes = new(recipes);
        ent.Comp.Modifiers = modifiers.Copy();
        if (string.IsNullOrWhiteSpace(ent.Comp.Modifiers.InstanceId))
            ent.Comp.Modifiers.InstanceId = Guid.NewGuid().ToString("N");
        Dirty(ent);
    }
}

using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Content.Shared.Emag.Systems;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared.Research.Components;
using Robust.Shared.Containers;
using Content.Shared.Examine;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Localizations;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using JetBrains.Annotations;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Lathe;

/// <summary>
/// This handles...
/// </summary>
public abstract class SharedLatheSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly SharedMaterialStorageSystem _materialStorage = default!;
    [Dependency] private readonly EmagSystem _emag = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;

    public readonly Dictionary<string, List<LatheRecipePrototype>> InverseRecipes = new();
    public const int MaxItemsPerRequest = 10_000;
    public const int ProductionTimeMultiplier = 3;

    public BlueprintModifiers? GetBlueprintModifiers(EntityUid uid, LatheRecipePrototype recipe, LatheComponent component)
    {
        if (component.BlueprintRecipes.TryGetValue(recipe.ID, out var selected))
            return selected;
        if (!TryComp<BlueprintReceiverComponent>(uid, out var receiver) ||
            !_containers.TryGetContainer(uid, receiver.ContainerId, out var container))
            return null;
        foreach (var blueprint in container.ContainedEntities)
            if (TryComp<BlueprintComponent>(blueprint, out var data) && data.Modifiers != null &&
                data.ProvidedRecipes.Contains(recipe.ID) && MatchesBlueprintLathe(uid, component, data.Modifiers))
                return data.Modifiers;
        return null;
    }

    public bool MatchesBlueprintLathe(EntityUid uid, LatheComponent component, BlueprintModifiers modifiers)
    {
        if (modifiers.Lathe == null)
            return true;
        if (component.LatheBlueprint == null || !_proto.TryIndex(component.LatheBlueprint, out var group))
            return false;
        var machine = MetaData(uid).EntityPrototype;
        if (machine == null)
            return false;
        var targets = modifiers.GetLathes().Where(group.Lathe.Contains).ToHashSet();
        return _proto.EnumerateParents(machine, includeSelf: true).Any(parent => targets.Contains(new EntProtoId(parent.ID)));
    }

    public Dictionary<ProtoId<MaterialPrototype>, int> GetMaterials(EntityUid uid, LatheRecipePrototype recipe, LatheComponent component)
        => BlueprintModifiers.Materials(recipe, GetBlueprintModifiers(uid, recipe, component), component.FinalMaterialUseMultiplier);

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmagLatheRecipesComponent, GotEmaggedEvent>(OnEmagged);
        SubscribeLocalEvent<EmagLatheRecipesComponent, GotUnEmaggedEvent>(OnUnemagged); // Frontier
        SubscribeLocalEvent<LatheComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<PrototypesReloadedEventArgs>(OnPrototypesReloaded);

        BuildInverseRecipeDictionary();
    }

    /// <summary>
    /// Get the set of all recipes that a lathe could possibly ever create (e.g., if all techs were unlocked).
    /// </summary>
    public HashSet<ProtoId<LatheRecipePrototype>> GetAllPossibleRecipes(LatheComponent component)
    {
        var recipes = new HashSet<ProtoId<LatheRecipePrototype>>();
        foreach (var pack in component.StaticPacks)
        {
            recipes.UnionWith(_proto.Index(pack).Recipes);
        }

        foreach (var pack in component.DynamicPacks)
        {
            recipes.UnionWith(_proto.Index(pack).Recipes);
        }

        if (component.LatheBlueprint is { } groupId && _proto.TryIndex(groupId, out var group))
        {
            var targets = group.Lathe.ToHashSet();
            foreach (var other in _proto.EnumeratePrototypes<LatheBlueprintPrototype>())
                if (other.Lathe.Any(group.Lathe.Contains)) targets.UnionWith(other.Lathe);
            foreach (var technology in _proto.EnumeratePrototypes<TechnologyBlueprintsPrototype>())
                if (targets.Contains(technology.Lathe) || technology.AdditionalLathes.Any(targets.Contains))
                {
                    recipes.UnionWith(technology.Recipe);
                    recipes.UnionWith(technology.RecipeForce);
                }
        }

        return recipes;
    }

    /// <summary>
    /// Add every recipe in the list of recipe packs to a single hashset.
    /// </summary>
    public void AddRecipesFromPacks(HashSet<ProtoId<LatheRecipePrototype>> recipes, IEnumerable<ProtoId<LatheRecipePackPrototype>> packs)
    {
        foreach (var id in packs)
        {
            var pack = _proto.Index(id);
            recipes.UnionWith(pack.Recipes);
        }
    }

    private void OnExamined(Entity<LatheComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (ent.Comp.ReagentOutputSlotId != null)
            args.PushMarkup(Loc.GetString("lathe-menu-reagent-slot-examine"));

        if (ent.Comp.ProductValueModifier != null) // Frontier
            args.PushMarkup(Loc.GetString($"lathe-product-value-modifier", ("modifier", ent.Comp.ProductValueModifier))); // Frontier

    }

    [PublicAPI]
    public bool CanProduce(EntityUid uid, string recipe, int amount = 1, LatheComponent? component = null)
    {
        return _proto.TryIndex<LatheRecipePrototype>(recipe, out var proto) && CanProduce(uid, proto, amount, component);
    }

    public bool CanProduce(EntityUid uid, LatheRecipePrototype recipe, int amount = 1, LatheComponent? component = null)
    {
        if (!Resolve(uid, ref component))
            return false;
        if (!HasRecipe(uid, recipe, component))
            return false;
        if (amount <= 0)
            return false;

        if (amount <= 0) // Frontier
            return false; // Frontier

        foreach (var (material, needed) in GetMaterials(uid, recipe, component))
        {
            if (_materialStorage.GetMaterialAmount(uid, material) < (long) needed * amount)
                return false;
        }
        return true;
    }

    private void OnEmagged(EntityUid uid, EmagLatheRecipesComponent component, ref GotEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
            return;

        if (_emag.CheckFlag(uid, EmagType.Interaction))
            return;

        args.Handled = true;
    }

    // Frontier: demag
    private void OnUnemagged(EntityUid uid, EmagLatheRecipesComponent component, ref GotUnEmaggedEvent args)
    {
        if (!_emag.CompareFlag(args.Type, EmagType.Interaction))
            return;

        if (!_emag.CheckFlag(uid, EmagType.Interaction))
            return;

        args.Handled = true;
    }
    // End Frontier: demag

    public static int AdjustMaterial(int original, bool reduce, float multiplier)
        => reduce ? (int) MathF.Ceiling(original * multiplier) : original;

    protected abstract bool HasRecipe(EntityUid uid, LatheRecipePrototype recipe, LatheComponent component);

    private void OnPrototypesReloaded(PrototypesReloadedEventArgs obj)
    {
        if (!obj.WasModified<LatheRecipePrototype>())
            return;
        BuildInverseRecipeDictionary();
    }

    private void BuildInverseRecipeDictionary()
    {
        InverseRecipes.Clear();
        foreach (var latheRecipe in _proto.EnumeratePrototypes<LatheRecipePrototype>())
        {
            if (latheRecipe.Result is not {} result)
                continue;

            InverseRecipes.GetOrNew(result).Add(latheRecipe);
        }
    }

    public bool TryGetRecipesFromEntity(string prototype, [NotNullWhen(true)] out List<LatheRecipePrototype>? recipes)
    {
        recipes = new();
        if (InverseRecipes.TryGetValue(prototype, out var r))
            recipes.AddRange(r);
        return recipes.Count != 0;
    }

    public string GetRecipeName(ProtoId<LatheRecipePrototype> proto)
    {
        return GetRecipeName(_proto.Index(proto));
    }

    public string GetRecipeName(LatheRecipePrototype proto)
    {
        if (!string.IsNullOrWhiteSpace(proto.Name))
            return Loc.GetString(proto.Name);

        if (proto.Result is {} result)
        {
            return _proto.Index(result).Name;
        }

        if (proto.ResultReagents is { } resultReagents)
        {
            return ContentLocalizationManager.FormatList(resultReagents
                .Select(p => Loc.GetString("lathe-menu-result-reagent-display", ("reagent", _proto.Index(p.Key).LocalizedName), ("amount", p.Value)))
                .ToList());
        }

        return string.Empty;
    }

    [PublicAPI]
    public string GetRecipeDescription(ProtoId<LatheRecipePrototype> proto)
    {
        return GetRecipeDescription(_proto.Index(proto));
    }

    public string GetRecipeDescription(LatheRecipePrototype proto)
    {
        if (!string.IsNullOrWhiteSpace(proto.Description))
            return Loc.GetString(proto.Description);

        if (proto.Result is {} result)
        {
            return _proto.Index(result).Description;
        }

        if (proto.ResultReagents is { } resultReagents)
        {
            // We only use the first one for the description since these descriptions don't combine very well.
            var reagent = resultReagents.First().Key;
            return _proto.Index(reagent).LocalizedDescription;
        }

        return string.Empty;
    }
}

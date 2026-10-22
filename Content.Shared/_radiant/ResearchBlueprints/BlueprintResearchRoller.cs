using System;
using System.Linq;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Shared._radiant.ResearchBlueprints;

public static class BlueprintResearchRoller
{
    public static HashSet<ProtoId<LatheRecipePrototype>> RollRecipes(
        IRobustRandom random, TechnologyBlueprintsPrototype technology)
    {
        var recipes = technology.RecipeForce.ToHashSet();
        if (technology.Recipe.Count > 0)
            recipes.Add(technology.Recipe[random.Next(technology.Recipe.Count)]);
        var previousCount = -1;
        while (previousCount != recipes.Count)
        {
            previousCount = recipes.Count;
            foreach (var linked in technology.RecipeLinks)
                if (linked.Any(recipes.Contains)) recipes.UnionWith(linked);
        }
        return recipes;
    }

    public static T PickWeighted<T>(IRobustRandom random, IReadOnlyList<T> items, Func<T, double> weight)
    {
        var total = items.Sum(weight);
        if (items.Count == 0 || !double.IsFinite(total) || total <= 0)
            throw new ArgumentException("Blueprint lottery requires positive finite weights.");
        var roll = random.NextDouble() * total;
        foreach (var item in items)
        {
            roll -= weight(item);
            if (roll < 0)
                return item;
        }
        return items[^1];
    }

    public static int MaterialAmount(IRobustRandom random, int minimum, int maximum)
    {
        if (maximum - minimum < 2 || minimum < 0)
            throw new ArgumentException("Blueprint material range must have an interior.");
        var sample = 0.0;
        for (var i = 0; i < 6; i++)
            sample += random.NextDouble();
        return Math.Clamp((int) Math.Round(minimum + (maximum - minimum) * sample / 6), minimum + 1, maximum - 1);
    }

    public static BlueprintModifiers Roll(IRobustRandom random, TechnologyBlueprintsPrototype technology,
        BlueprintResearchPrototype settings, IReadOnlyList<BlueprintMaterialTierPrototype> tiers,
        IReadOnlyList<EntProtoId> machines, HashSet<ProtoId<MaterialPrototype>> required)
    {
        var result = new BlueprintModifiers
        {
            InstanceId = Guid.NewGuid().ToString("N"), Technology = technology.ID,
            RareLoc = technology.RareLoc, Lathe = technology.Lathe,
            AdditionalLathes = new(technology.AdditionalLathes),
        };
        var availableTiers = tiers.Where(t => t.Weight > 0 && t.Materials.Any(m => !required.Contains(m))).ToList();
        void Time() => result.TimeMultiplier = random.Next(2, 8);
        void Material() => result.MaterialMultiplier = random.Next(2, 12);
        void Extra()
        {
            if (availableTiers.Count == 0)
                return;
            var tier = PickWeighted(random, availableTiers, t => t.Weight);
            var materials = tier.Materials.Where(m => !required.Contains(m)).ToArray();
            result.ExtraMaterials[materials[random.Next(materials.Length)]] = MaterialAmount(random, tier.Minimum, tier.Maximum);
        }
        if (random.NextDouble() < settings.TimeChance) Time();
        if (random.NextDouble() < settings.MaterialChance) Material();
        if (random.NextDouble() < settings.ExtraMaterialChance) Extra();
        var alternatives = machines.Where(m => !result.GetLathes().Contains(m)).Distinct().ToArray();
        if (alternatives.Length > 0 && random.NextDouble() < settings.LatheChance)
        {
            result.Lathe = alternatives[random.Next(alternatives.Length)];
            result.AdditionalLathes.Clear();
        }
        var unmodifiedChance = (double)settings.UnmodifiedChance;

        switch (technology.RareLoc.ToString())
        {
            case "research-technology-blueprint-common":
                unmodifiedChance *= 3;
                break;

            case "research-technology-blueprint-epic":
                unmodifiedChance = 0;
                break;
        }

        unmodifiedChance = Math.Clamp(unmodifiedChance, 0.0, 1.0);

        if (result.TimeMultiplier == 1 && result.MaterialMultiplier == 1 && result.ExtraMaterials.Count == 0 &&
            result.Lathe == technology.Lathe && random.NextDouble() >= unmodifiedChance)
        {
            var choices = new List<(Action Apply, double Weight)>();
            if (settings.TimeChance > 0) choices.Add((Time, settings.TimeChance));
            if (settings.MaterialChance > 0) choices.Add((Material, settings.MaterialChance));
            if (settings.ExtraMaterialChance > 0 && availableTiers.Count > 0) choices.Add((Extra, settings.ExtraMaterialChance));
            if (choices.Count > 0) PickWeighted(random, choices, c => c.Weight).Apply();
        }
        return result;
    }
}

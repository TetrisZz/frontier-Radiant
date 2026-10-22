using System;
using System.Collections.Generic;
using System.Linq;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using NUnit.Framework;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class BlueprintResearchTest
{
    [Test]
    public void RelatedRecipesFollowTheSelectedItemWithoutAddingUnrelatedChoices()
    {
        var random = new RobustRandom();
        random.SetSeed(42);
        var technology = new TechnologyBlueprintsPrototype
        {
            Recipe = ["Left", "Right", "Other"], RecipeForce = ["Required"], RecipeLinks = [["Left", "Right"]],
        };
        var paired = 0;
        var single = 0;
        for (var i = 0; i < 1000; i++)
        {
            var recipes = BlueprintResearchRoller.RollRecipes(random, technology);
            Assert.That(recipes, Does.Contain(new ProtoId<LatheRecipePrototype>("Required")));
            if (recipes.Contains("Left"))
            {
                Assert.That(recipes, Is.EquivalentTo(new ProtoId<LatheRecipePrototype>[] { "Left", "Right", "Required" }));
                paired++;
            }
            else
            {
                Assert.That(recipes, Is.EquivalentTo(new ProtoId<LatheRecipePrototype>[] { "Other", "Required" }));
                single++;
            }
        }
        Assert.That(paired, Is.GreaterThan(0));
        Assert.That(single, Is.GreaterThan(0));
        Assert.That(technology.Recipe, Has.Count.EqualTo(3));
        Assert.That(technology.RecipeForce, Has.Count.EqualTo(1));
        Assert.That(technology.RecipeLinks.Single(), Has.Count.EqualTo(2));
    }

    [Test]
    public void ForcedRecipesAndOverlappingLinksExpandWithoutDuplicates()
    {
        var random = new RobustRandom();
        random.SetSeed(42);
        var technology = new TechnologyBlueprintsPrototype
        {
            RecipeForce = ["Left"], Recipe = ["Right", "Third", "Other"],
            RecipeLinks = [["Right", "Third"], ["Left", "Right"]],
        };
        for (var i = 0; i < 100; i++)
        {
            var recipes = BlueprintResearchRoller.RollRecipes(random, technology);
            Assert.That(recipes.IsSupersetOf(new ProtoId<LatheRecipePrototype>[] { "Left", "Right", "Third" }), Is.True);
            Assert.That(recipes.Count, Is.InRange(3, 4));
        }
        Assert.That(BlueprintResearchRoller.RollRecipes(random, new TechnologyBlueprintsPrototype
        {
            RecipeForce = ["First", "Second"],
        }), Is.EquivalentTo(new ProtoId<LatheRecipePrototype>[] { "First", "Second" }));
    }

    [Test]
    public void PriceAndTimeIncludeBlueprintMachineAndGlobalModifiers()
    {
        var recipe = new LatheRecipePrototype { CompleteTime = TimeSpan.FromSeconds(5), Materials = new() { ["Plastic"] = 200 } };
        var modifiers = new BlueprintModifiers { MaterialMultiplier = 3, TimeMultiplier = 4, ExtraMaterials = new() { ["Silver"] = 101 } };
        var materials = BlueprintModifiers.Materials(recipe, modifiers, 0.85f);
        Assert.That(materials["Plastic"], Is.EqualTo(510));
        Assert.That(materials["Silver"], Is.EqualTo(86));
        Assert.That(BlueprintModifiers.Time(recipe, modifiers, 0.5f) * SharedLatheSystem.ProductionTimeMultiplier, Is.EqualTo(TimeSpan.FromSeconds(30)));
        Assert.That(SharedLatheSystem.ProductionTimeMultiplier, Is.EqualTo(3));
        recipe.ApplyMaterialDiscount = false;
        Assert.That(BlueprintModifiers.Materials(recipe, modifiers, 0.1f)["Plastic"], Is.EqualTo(600));
        Assert.That(recipe.Materials, Has.Count.EqualTo(1), "Calculations must not modify shared prototypes");
    }

    [Test]
    public void QueueSnapshotDoesNotFollowSubsequentBlueprintChanges()
    {
        var original = new BlueprintModifiers { InstanceId = "first", TimeMultiplier = 2, ExtraMaterials = new() { ["Gold"] = 300 },
            Lathe = "MercenaryTechFab", AdditionalLathes = ["MercenaryTechFabHacked", "NfsdTechFab"] };
        var batch = new LatheRecipeBatch("Test", 0, 2) { Modifiers = original.Copy() };
        original.TimeMultiplier = 5;
        original.ExtraMaterials["Gold"] = 5000;
        original.AdditionalLathes.Clear();
        Assert.That(batch.Modifiers!.TimeMultiplier, Is.EqualTo(2));
        Assert.That(batch.Modifiers.ExtraMaterials["Gold"], Is.EqualTo(300));
        Assert.That(batch.Modifiers.GetLathes(), Is.EquivalentTo(new EntProtoId[] { "MercenaryTechFab", "MercenaryTechFabHacked", "NfsdTechFab" }));
    }

    [Test]
    public void CompatibleMachinesAreCopiedAndMachineModifierReplacesTheWholeSet()
    {
        var random = new RobustRandom();
        random.SetSeed(42);
        var technology = new TechnologyBlueprintsPrototype
        {
            Lathe = "MercenaryTechFab", AdditionalLathes = ["MercenaryTechFabHacked", "NfsdTechFab"],
        };
        typeof(TechnologyBlueprintsPrototype).GetProperty(nameof(TechnologyBlueprintsPrototype.ID))!.SetValue(technology, "Test");
        var settings = new BlueprintResearchPrototype { TimeChance = 0, MaterialChance = 0, ExtraMaterialChance = 0, LatheChance = 0 };
        EntProtoId[] machines = ["MercenaryTechFab", "MercenaryTechFabHacked", "NfsdTechFab", "AmmoTechFab"];
        var normal = BlueprintResearchRoller.Roll(random, technology, settings, [], machines, new());
        Assert.That(normal.GetLathes(), Is.EquivalentTo(machines.Take(3)));
        settings.LatheChance = 1;
        var changed = BlueprintResearchRoller.Roll(random, technology, settings, [], machines, new());
        Assert.That(changed.GetLathes(), Is.EquivalentTo(new EntProtoId[] { "AmmoTechFab" }));
        var noAlternatives = BlueprintResearchRoller.Roll(random, technology, settings, [], machines.Take(3).ToArray(), new());
        Assert.That(noAlternatives.GetLathes(), Is.EquivalentTo(machines.Take(3)));
        normal.AdditionalLathes.Clear();
        Assert.That(technology.AdditionalLathes, Has.Count.EqualTo(2), "Blueprint changes must not mutate prototypes");
    }

    [TestCase(200, 10000)]
    [TestCase(100, 5000)]
    [TestCase(10, 1000)]
    [TestCase(134, 6667)]
    [TestCase(67, 3334)]
    [TestCase(7, 667)]
    public void AdditionalMaterialsFavorTheMiddleAndNeverReachEndpoints(int minimum, int maximum)
    {
        var random = new RobustRandom();
        random.SetSeed(13);
        var amounts = Enumerable.Range(0, 20000).Select(_ => BlueprintResearchRoller.MaterialAmount(random, minimum, maximum)).ToArray();
        Assert.That(amounts.Min(), Is.GreaterThan(minimum));
        Assert.That(amounts.Max(), Is.LessThan(maximum));
        var middle = amounts.Count(a => a >= minimum + (maximum - minimum) * 0.3 && a <= minimum + (maximum - minimum) * 0.7);
        Assert.That(middle / (double) amounts.Length, Is.GreaterThan(0.85));
    }

    [Test]
    public void LotteryAlwaysProducesAnEntryAndHonorsRelativeRarity()
    {
        var random = new RobustRandom();
        random.SetSeed(42);
        var items = new[] { 1, 1000 };
        var results = Enumerable.Range(0, 100000).Select(_ => BlueprintResearchRoller.PickWeighted(random, items, value => 1.0 / value)).ToArray();
        Assert.That(results.All(value => items.Contains(value)), Is.True);
        Assert.That(results.Count(value => value == 1000), Is.InRange(50, 150));
    }

    [Test]
    public void ModifierRatesKeepCleanBlueprintsUnderOnePercentAndMachineChangesUnderTen()
    {
        var random = new RobustRandom();
        random.SetSeed(25);
        var technology = new TechnologyBlueprintsPrototype { Lathe = "AmmoTechFab" };
        typeof(TechnologyBlueprintsPrototype).GetProperty(nameof(TechnologyBlueprintsPrototype.ID))!.SetValue(technology, "Test");
        var tier = new BlueprintMaterialTierPrototype { Materials = ["Silver"], Minimum = 100, Maximum = 5000 };
        var required = new HashSet<ProtoId<MaterialPrototype>> { "Plastic" };
        var results = Enumerable.Range(0, 30000).Select(_ => BlueprintResearchRoller.Roll(random, technology,
            new BlueprintResearchPrototype(), [tier], new EntProtoId[] { "AmmoTechFab", "MercenaryTechFab" }, required)).ToArray();
        Assert.That(results.Count(m => m.TimeMultiplier == 1 && m.MaterialMultiplier == 1 && m.ExtraMaterials.Count == 0 && m.Lathe == technology.Lathe), Is.LessThan(300));
        Assert.That(results.Count(m => m.Lathe != technology.Lathe), Is.LessThan(3000));
        Assert.That(results.All(m => m.TimeMultiplier is >= 1 and <= 7 && m.MaterialMultiplier is >= 1 and <= 11), Is.True);
        Assert.That(results.Select(m => m.InstanceId).Distinct().Count(), Is.EqualTo(results.Length));
    }

    [Test]
    public void BlueprintCanParticipateInSeveralTiers()
    {
        var technology = new TechnologyBlueprintsPrototype { Tier = 1, Tiers = [2, 3] };
        Assert.That(new[] { 1, 2, 3 }.All(technology.SupportsTier), Is.True);
        Assert.That(technology.SupportsTier(4), Is.False);
    }

    [Test]
    public void PresetFiltersRarityAndTierTogether()
    {
        var preset = new BlueprintResearchPrototype
        {
            Tier = 2, AllowedRarities = ["research-technology-blueprint-common"],
        };
        var technology = new TechnologyBlueprintsPrototype
        {
            Tier = 1, Tiers = [2], Recipe = ["Test"], RareLoc = "research-technology-blueprint-common",
            Discipline = "NFEngineering",
        };
        Assert.That(preset.Allows(technology), Is.True);
        technology.RareLoc = "research-technology-blueprint-rare";
        Assert.That(preset.Allows(technology), Is.False);
        preset.AllowedRarities.Clear();
        Assert.That(preset.Allows(technology), Is.True);
        technology.Tiers.Clear();
        Assert.That(preset.Allows(technology), Is.False);
        technology.Tier = 2;
        technology.Rare = 0;
        Assert.That(preset.Allows(technology), Is.False);
    }

    [Test]
    public void DisciplineSelectsConfiguredBlueprintWithFallback()
    {
        var preset = new BlueprintResearchPrototype
        {
            DisciplineBlueprints = new() { ["NFScience"] = "NFBlueprintScience" },
        };
        var technology = new TechnologyBlueprintsPrototype { Discipline = "NFScience" };
        Assert.That(preset.BlueprintFor(technology).Id, Is.EqualTo("NFBlueprintScience"));
        technology.Discipline = "NFEngineering";
        Assert.That(preset.BlueprintFor(technology), Is.EqualTo(preset.Blueprint));
    }

    [Test]
    public void FixedRecipeModifiersUseTheSameRollerWithoutInventingResearchRarity()
    {
        var random = new RobustRandom();
        random.SetSeed(55);
        var basis = new BlueprintModifiers { Lathe = "MercenaryTechFab", AdditionalLathes = ["NfsdTechFab"] };
        var settings = new BlueprintResearchPrototype { TimeChance = 1, MaterialChance = 1, ExtraMaterialChance = 0, LatheChance = 0 };
        var first = BlueprintResearchRoller.Roll(random, settings, [], [], [], basis);
        var second = BlueprintResearchRoller.Roll(random, settings, [], [], [], basis);
        Assert.That(first.TimeMultiplier, Is.InRange(2, 7));
        Assert.That(first.MaterialMultiplier, Is.InRange(2, 11));
        Assert.That(first.RareLoc.Id, Is.EqualTo("research-technology-blueprint-unique"));
        Assert.That(first.Technology, Is.Null);
        Assert.That(first.GetLathes(), Is.EquivalentTo(basis.GetLathes()));
        Assert.That(first.InstanceId, Is.Not.Empty.And.Not.EqualTo(second.InstanceId));
        Assert.That(basis.InstanceId, Is.Empty);
        Assert.That(basis.TimeMultiplier, Is.EqualTo(1));
    }

    [Test]
    public void FaxPrintoutKeepsAnIndependentSnapshotOfRecipesAndModifiers()
    {
        var recipes = new HashSet<ProtoId<LatheRecipePrototype>> { "NFWeaponSubMachineGunC20r" };
        var modifiers = new BlueprintModifiers
        {
            InstanceId = "fixed-instance", TimeMultiplier = 4, MaterialMultiplier = 3,
            ExtraMaterials = new() { ["Silver"] = 100 },
        };
        var printout = new Content.Shared.Fax.Components.FaxPrintout("", "Blueprint",
            blueprintRecipes: recipes, blueprintModifiers: modifiers);
        recipes.Clear();
        modifiers.ExtraMaterials.Clear();
        modifiers.TimeMultiplier = 1;
        Assert.That(printout.BlueprintRecipes.Select(r => r.Id), Is.EqualTo(new[] { "NFWeaponSubMachineGunC20r" }));
        Assert.That(printout.BlueprintModifiers!.InstanceId, Is.EqualTo("fixed-instance"));
        Assert.That(printout.BlueprintModifiers.TimeMultiplier, Is.EqualTo(4));
        Assert.That(printout.BlueprintModifiers.ExtraMaterials["Silver"], Is.EqualTo(100));
    }
}

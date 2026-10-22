using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Client._radiant.ResearchBlueprints;
using Content.Server._radiant.ResearchBlueprints;
using Content.Server.Lathe;
using Content.Server.Materials;
using Content.Server.Power.Components;
using Content.Server.Research.Components;
using Content.Server.Research.Systems;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared._radiant.Skills;
using Content.Shared.Lathe;
using Content.Shared.Interaction.Components;
using Content.Shared.Lathe.Prototypes;
using Content.Shared.Examine;
using Content.Shared.Materials;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Localization;
using Robust.Shared.Utility;
using Robust.Shared.Maths;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface;

namespace Content.IntegrationTests.Tests;

[TestFixture]
public sealed class ResearchBlueprintTest
{
    [Test]
    public async Task CatalogueRollSelectionQueueSnapshotAndEjection()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true, Connected = true });
        var map = await pair.CreateTestMap();
        BlueprintLibraryState? windowState = null;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var proto = pair.Server.ProtoMan;
            var system = em.System<BlueprintResearchSystem>();
            var research = em.System<ResearchSystem>();
            var lathes = em.System<LatheSystem>();
            var containers = em.System<SharedContainerSystem>();
            var settings = proto.Index<BlueprintResearchPrototype>("Default");
            var catalogue = proto.EnumeratePrototypes<TechnologyBlueprintsPrototype>().ToArray();
            var activeTechnologies = proto.EnumeratePrototypes<TechnologyPrototype>()
                .Where(t => t.Discipline.Id.StartsWith("NF", StringComparison.Ordinal) && !t.Hidden).ToArray();
            var eligibleRecipes = catalogue.Where(t => t.SupportsTier(settings.Tier) && double.IsFinite(t.Rare) && t.Rare > 0)
                .SelectMany(t => t.Recipe.Concat(t.RecipeForce)).ToHashSet();
            foreach (var technology in activeTechnologies)
            {
                // A technology may be split into several blueprint groups, such as bluespace storage.
                Assert.That(technology.RecipeUnlocks.Except(eligibleRecipes), Is.Empty,
                    $"All recipes of {technology.ID} must be obtainable from the current console pool");
            }
            Assert.That(catalogue.Where(t => t.Discipline.Id == "Acorp" || t.ID.StartsWith("Acorp", StringComparison.Ordinal)), Is.Empty);
            var activeRecipes = activeTechnologies.SelectMany(t => t.RecipeUnlocks).ToHashSet();
            var exclusiveAcorpRecipes = proto.EnumeratePrototypes<TechnologyPrototype>()
                .Where(t => t.Discipline.Id == "Acorp" || t.ID.StartsWith("Acorp", StringComparison.Ordinal))
                .SelectMany(t => t.RecipeUnlocks).Except(activeRecipes).ToHashSet();
            Assert.That(catalogue.SelectMany(t => t.Recipe.Concat(t.RecipeForce)).Where(exclusiveAcorpRecipes.Contains), Is.Empty,
                "Acorp-exclusive recipes must not leak into the public blueprint pool");
            TestContext.Out.WriteLine($"Blueprint coverage: {activeTechnologies.Length} active technologies, {activeRecipes.Count} recipes; Acorp excluded.");
            var missingRecipes = new List<string>();
            var recipeRandom = new RobustRandom();
            recipeRandom.SetSeed(42);
            foreach (var technology in proto.EnumeratePrototypes<TechnologyBlueprintsPrototype>())
            {
                var declared = technology.Recipe.Concat(technology.RecipeForce).ToHashSet();
                foreach (var linked in technology.RecipeLinks)
                {
                    Assert.That(linked.Distinct().Count(), Is.GreaterThanOrEqualTo(2), technology.ID);
                    Assert.That(linked.All(declared.Contains), Is.True, $"{technology.ID}: linked recipes must be declared in recipe or recipeForce");
                }
                foreach (var left in declared.Where(r => r.Id.StartsWith("LeftArm", StringComparison.Ordinal)))
                {
                    ProtoId<LatheRecipePrototype> right = "RightArm" + left.Id[7..];
                    if (proto.HasIndex(right))
                        Assert.That(technology.RecipeLinks.Any(linked => linked.Contains(left) && linked.Contains(right)), Is.True,
                            $"{technology.ID}: {left} must be linked to {right}");
                }
                if (technology.RecipeLinks.Count > 0)
                {
                    var seen = new HashSet<ProtoId<LatheRecipePrototype>>();
                    for (var i = 0; i < 1000; i++)
                    {
                        var recipes = BlueprintResearchRoller.RollRecipes(recipeRandom, technology);
                        Assert.That(recipes.IsSupersetOf(technology.RecipeForce), Is.True, technology.ID);
                        Assert.That(recipes.IsSubsetOf(declared), Is.True, technology.ID);
                        foreach (var linked in technology.RecipeLinks.Where(linked => linked.Any(recipes.Contains)))
                            Assert.That(recipes.IsSupersetOf(linked), Is.True, technology.ID);
                        seen.UnionWith(recipes);
                    }
                    Assert.That(seen, Is.EquivalentTo(declared), $"{technology.ID}: all choices must remain reachable");
                }
                Assert.That(technology.Rare, Is.GreaterThan(0));
                Assert.That(proto.HasIndex(technology.EntityIcon), Is.True, technology.ID);
                Assert.That(proto.HasIndex(technology.Discipline), Is.True, technology.ID);
                foreach (var target in technology.AdditionalLathes.Prepend(technology.Lathe))
                {
                    var machine = proto.Index(target);
                    Assert.That(machine.TryGetComponent<LatheComponent>(out var machineLathe, em.ComponentFactory), Is.True, technology.ID);
                    Assert.That(machineLathe!.LatheBlueprint, Is.Not.Null, technology.ID);
                    Assert.That(proto.Index(machineLathe.LatheBlueprint.Value).Lathe, Does.Contain(target), technology.ID);
                }
                if (technology.Discipline.Id == "NFArsenalMercenary")
                    Assert.That(technology.AdditionalLathes.Prepend(technology.Lathe),
                        Is.EquivalentTo(new EntProtoId[] { "MercenaryTechFab", "MercenaryTechFabHacked", "NfsdTechFab" }), technology.ID);
                foreach (var recipe in technology.Recipe.Concat(technology.RecipeForce))
                {
                    if (!proto.HasIndex(recipe))
                    {
                        missingRecipes.Add($"{technology.ID}: {recipe}");
                        continue;
                    }
                    var recipePrototype = proto.Index(recipe);
                    Assert.That(recipePrototype.Result, Is.Not.Null, $"{technology.ID}: {recipe}");
                    Assert.That(proto.HasIndex(recipePrototype.Result!.Value), Is.True, $"{technology.ID}: {recipe} result");
                }
            }
            Assert.That(missingRecipes, Is.Empty, string.Join("; ", missingRecipes));
            foreach (var (technologyId, left, right) in new[]
            {
                ("NFRipleyAPLU", "RipleyLArm", "RipleyRArm"), ("NFRipleyAPLU", "RipleyLLeg", "RipleyRLeg"),
                ("NFCritterMechs", "HamtrLArm", "HamtrRArm"), ("NFCritterMechs", "HamtrLLeg", "HamtrRLeg"),
                ("NFHONKMech", "HonkerLArm", "HonkerRArm"), ("NFHONKMech", "HonkerLLeg", "HonkerRLeg"),
                ("NFClarke", "ClarkeLArm", "ClarkeRArm"),
            })
                Assert.That(proto.Index<TechnologyBlueprintsPrototype>(technologyId).RecipeLinks.Any(linked => linked.Contains(left) && linked.Contains(right)), Is.True);
            foreach (var id in new[] { "NFBasicRoboticsEquipment", "NFBasicRobotics", "NFVim", "NFRoboticsEquipment", "NFRipleyAPLU", "NFCritterMechs", "NFAdvancedRoboticsEquipment", "NFHONKMech", "NFHornRoboticsEquipment" })
                Assert.That(proto.Index<TechnologyBlueprintsPrototype>(id).Lathe.Id, Is.EqualTo("ExosuitFabricator"));
            Assert.That(proto.Index<TechnologyBlueprintsPrototype>("BluespaceStorage").AdditionalLathes,
                Does.Contain(new EntProtoId("Protolathe")));
            foreach (var (id, minimum, maximum) in new[] { ("Common", 134, 6667), ("Uncommon", 67, 3334), ("Rare", 7, 667) })
            {
                var tier = proto.Index<BlueprintMaterialTierPrototype>(id);
                Assert.That(tier.Minimum, Is.EqualTo(minimum));
                Assert.That(tier.Maximum, Is.EqualTo(maximum));
            }
            foreach (var surgery in proto.EnumeratePrototypes<TechnologyPrototype>().Where(t => t.ID.StartsWith("Starlight", StringComparison.Ordinal)))
            {
                Assert.That(proto.TryIndex<TechnologyBlueprintsPrototype>(surgery.ID, out var blueprintTechnology), Is.True, surgery.ID);
                Assert.That(blueprintTechnology!.Recipe.Concat(blueprintTechnology.RecipeForce), Is.EquivalentTo(surgery.RecipeUnlocks), surgery.ID);
                Assert.That(blueprintTechnology.SupportsTier(1), Is.True, surgery.ID);
                if (surgery.Discipline.Id == "NFMedical")
                    Assert.That(blueprintTechnology.Lathe.Id, Is.EqualTo("MedicalTechFab"), surgery.ID);
            }
            EntityUid Spawn(string id)
            {
                var entity = em.SpawnEntity(id, map.GridCoords);
                if (em.TryGetComponent<ApcPowerReceiverComponent>(entity, out var power))
                {
                    power.NeedsPower = false;
                    power.Powered = true;
                }
                return entity;
            }
            Spawn("BlueprintLithographLegacy");
            foreach (var id in new[] { "Protolathe", "AmmoTechFab", "SecurityTechFab" })
            {
                var entity = Spawn(id);
                var component = em.GetComponent<LatheComponent>(entity);
                foreach (var pack in component.StaticPacks.Concat(component.DynamicPacks))
                    foreach (var recipe in proto.Index(pack).Recipes)
                        Assert.That(proto.HasIndex(recipe), Is.True, $"{id}: {pack}: {recipe}");

                // Simulate a stale reference in an old pack and in a saved server selection.
                ProtoId<LatheRecipePrototype> missingRecipe = "RemovedBlueprintRecipeForTest";
                var packPrototype = proto.Index<LatheRecipePackPrototype>("NFProtolathe");
                var originalPacks = component.StaticPacks.ToList();
                try
                {
                    packPrototype.Recipes.Add(missingRecipe);
                    component.StaticPacks.Add("NFProtolathe");
                    foreach (var selected in new[] { false, true })
                    {
                        if (selected) component.BlueprintRecipes[missingRecipe] = new BlueprintModifiers();
                        foreach (var unavailable in new[] { false, true })
                        {
                            var available = lathes.GetAvailableRecipes(entity, component, unavailable);
                            Assert.That(available, Does.Not.Contain(missingRecipe));
                            Assert.That(available, Does.Contain(new ProtoId<LatheRecipePrototype>("NFRiotShield")));
                        }
                        lathes.UpdateUserInterfaceState(entity, component);
                        em.System<MaterialStorageSystem>().UpdateMaterialWhitelist(entity);
                    }
                }
                finally
                {
                    packPrototype.Recipes.Remove(missingRecipe);
                    component.StaticPacks = originalPacks;
                    component.BlueprintRecipes.Remove(missingRecipe);
                }
            }
            TestContext.Out.WriteLine("Lathe map initialization and stale recipe assertions completed.");
            var server = Spawn("ResearchAndDevelopmentServer");
            var library = Spawn("BlueprintLithograph");
            var machineUid = Spawn("EngineeringTechFab");
            var secondMachine = Spawn("EngineeringTechFab");
            var otherMachine = Spawn("MedicalTechFab");
            var console = Spawn("ComputerResearchAndDevelopment");
            var skillSystem = em.System<SharedProfessionalSkillsSystem>();
            var student = em.SpawnEntity(null, map.GridCoords);
            em.AddComponent<ComplexInteractionComponent>(student);
            var skills = em.AddComponent<ProfessionalSkillsComponent>(student);
            Assert.That(ProfessionalSkillRules.Maximum(ProfessionalSkill.Science), Is.EqualTo(3));
            Assert.That(skillSystem.Requirement(console, SkillAction.Interface, nameof(ResearchBlueprintRollMessage))!.Level, Is.EqualTo(1));
            Assert.That(skillSystem.Requirement(console, SkillAction.Interface, nameof(ConsoleUnlockTechnologyMessage))!.Level, Is.EqualTo(3));
            for (var level = 0; level <= 3; level++)
            {
                skills.Levels[(int) ProfessionalSkill.Science] = level;
                var attempt = new BoundUserInterfaceMessageAttempt(student, console, ResearchConsoleUiKey.Key, new ResearchBlueprintRollMessage());
                em.EventBus.RaiseEvent(EventSource.Local, attempt);
                if (!attempt.Cancelled) em.EventBus.RaiseLocalEvent(console, attempt);
                Assert.That(attempt.Cancelled, Is.EqualTo(level < 1), $"Research blueprint skill level {level}");
            }
            Assert.That(proto.Index<ProfessionalSkillRequirementPrototype>("Artifacts").Level, Is.EqualTo(2));
            Assert.That(proto.Index<ProfessionalSkillRequirementPrototype>("Anomalies").Level, Is.EqualTo(3));
            Assert.That(proto.Index<ProfessionalSkillRequirementPrototype>("ExperimentalAnomalies").Level, Is.EqualTo(3));
            var serverComp = em.GetComponent<ResearchServerComponent>(server);
            foreach (var client in new[] { library, machineUid, secondMachine, otherMachine, console })
            {
                research.UnregisterClient(client);
                research.RegisterClient(client, server);
                Assert.That(em.GetComponent<ResearchClientComponent>(client).Server, Is.EqualTo(server));
            }
            serverComp.Points = settings.Cost;
            var consoleComp = em.GetComponent<ResearchConsoleComponent>(console);
            Assert.That(system.TryRoll(console, consoleComp), Is.True);
            Assert.That(serverComp.Points, Is.Zero);
            var rolled = em.EntityQuery<BlueprintComponent>().Where(b => b.Modifiers?.Technology != null).ToArray();
            Assert.That(rolled, Has.Length.EqualTo(1));
            var roll = rolled.Single();
            var technologyProto = proto.Index(roll.Modifiers!.Technology!.Value);
            Assert.That(em.GetComponent<MetaDataComponent>(roll.Owner).EntityPrototype!.ID,
                Is.EqualTo(settings.BlueprintFor(proto.Index(roll.Modifiers!.Technology!.Value)).Id));
            Assert.That(em.GetComponent<MetaDataComponent>(roll.Owner).EntityName, Does.Not.Contain(roll.Modifiers.ShortId));
            Assert.That(roll.ProvidedRecipes.ToHashSet().IsSupersetOf(technologyProto.RecipeForce), Is.True);
            Assert.That(roll.ProvidedRecipes.Count, Is.GreaterThan(0));
            var historyResult = serverComp.BlueprintResults.Single();
            Assert.That(historyResult.Recipes, Is.EquivalentTo(roll.ProvidedRecipes));
            Assert.That(historyResult.RareLoc, Is.EqualTo(roll.Modifiers.RareLoc));
            Assert.That(historyResult.InstanceId, Is.EqualTo(roll.Modifiers.InstanceId));
            Assert.That(system.TryRoll(console, consoleComp), Is.False);
            Assert.That(serverComp.Points, Is.Zero);
            var ripperTechnology = proto.Index<TechnologyBlueprintsPrototype>("StarlightAdvancedSurgery");
            var originalTier = settings.Tier;
            var originalChoices = ripperTechnology.Recipe;
            const int pairedTestTier = 987654;
            try
            {
                settings.Tier = pairedTestTier;
                ripperTechnology.Tiers.Add(pairedTestTier);
                ripperTechnology.Recipe = ["RightArmCyberRipper"];
                serverComp.Points = settings.Cost;
                Assert.That(system.TryRoll(console, consoleComp), Is.True);
                Assert.That(serverComp.Points, Is.Zero);
                var pairedResult = serverComp.BlueprintResults[0];
                var pairedBlueprint = em.EntityQuery<BlueprintComponent>().Single(b => b.Modifiers?.InstanceId == pairedResult.InstanceId);
                ProtoId<LatheRecipePrototype>[] pairRecipes = ["LeftArmCyberRipper", "RightArmCyberRipper"];
                Assert.That(pairedBlueprint.ProvidedRecipes, Is.EquivalentTo(pairRecipes));
                Assert.That(pairedResult.Recipes, Is.EquivalentTo(pairRecipes));
                Assert.That(pairedResult.RareLoc, Is.EqualTo(pairedBlueprint.Modifiers!.RareLoc));
                var pairedName = em.GetComponent<MetaDataComponent>(pairedBlueprint.Owner).EntityName;
                foreach (var recipe in pairRecipes)
                    Assert.That(pairedName, Does.Contain(lathes.GetRecipeName(proto.Index(recipe))));
            }
            finally
            {
                settings.Tier = originalTier;
                ripperTechnology.Tiers.Remove(pairedTestTier);
                ripperTechnology.Recipe = originalChoices;
            }
            EntityUid Blueprint(string id, int multiplier, string recipe = "ClothingBackpackHolding", string machine = "EngineeringTechFab")
            {
                var entity = Spawn("ResearchRandomBlueprint");
                var blueprint = em.GetComponent<BlueprintComponent>(entity);
                system.SetBlueprint((entity, blueprint), [recipe], new BlueprintModifiers
                {
                    InstanceId = id, Lathe = machine, TimeMultiplier = multiplier, MaterialMultiplier = multiplier,
                    ExtraMaterials = new() { ["Silver"] = 100 },
                });
                Assert.That(em.System<Content.Shared.Research.Systems.BlueprintSystem>().TryInsertBlueprint(
                    (library, em.GetComponent<BlueprintReceiverComponent>(library)), (entity, blueprint), null), Is.True);
                return entity;
            }
            var first = Blueprint("first", 2);
            var second = Blueprint("second", 3);
            var examine = new ExaminedEvent(new FormattedMessage(), first, first, true, false);
            em.System<Content.Shared.Research.Systems.BlueprintSystem>().OnBlueprintExamined((first, em.GetComponent<BlueprintComponent>(first)), ref examine);
            examine.PushText("Standard size marker");
            var description = examine.GetTotalMessage().ToString();
            Assert.That(description, Does.Contain(Loc.GetString("blueprint-description")));
            Assert.That(description, Does.Contain(lathes.GetRecipeName(proto.Index<LatheRecipePrototype>("ClothingBackpackHolding"))));
            Assert.That(description, Does.Not.Contain("EngineeringTechFab"));
            Assert.That(description, Does.Not.Contain("FIRST"));
            Assert.That(description, Does.Not.Contain(Loc.GetString("research-blueprint-modifiers", ("time", 2), ("materials", 2))));
            Assert.That(description, Does.EndWith(Loc.GetString("research-technology-blueprint-unique")));
            Assert.That(system.TrySelectBlueprint(library, first, "ClothingBackpackHolding"), Is.True);
            Assert.That(em.System<SharedUserInterfaceSystem>().TryGetUiState<BlueprintLibraryState>(library, BlueprintLibraryUiKey.Key, out windowState), Is.True);
            Assert.That(windowState!.ServerName, Is.EqualTo(serverComp.ServerName));
            Assert.That(windowState.ServerId, Is.EqualTo(serverComp.Id));
            var destinations = windowState.Blueprints.Single(b => b.Entity == em.GetNetEntity(first)).Destinations["ClothingBackpackHolding"];
            Assert.That(destinations.Select(d => d.Entity), Is.EquivalentTo(new[] { em.GetNetEntity(machineUid), em.GetNetEntity(secondMachine) }));
            Assert.That(destinations.All(d => d.Prototype.Id == "EngineeringTechFab"), Is.True);
            var lathe = em.GetComponent<LatheComponent>(machineUid);
            Assert.That(lathe.BlueprintRecipes["ClothingBackpackHolding"].InstanceId, Is.EqualTo("first"));
            Assert.That(em.GetComponent<LatheComponent>(otherMachine).BlueprintRecipes, Is.Empty);
            var recipeProto = proto.Index<LatheRecipePrototype>("ClothingBackpackHolding");
            var materials = em.System<MaterialStorageSystem>();
            foreach (var (material, amount) in lathes.GetMaterials(machineUid, recipeProto, lathe))
                Assert.That(materials.TryChangeMaterialAmount(machineUid, material, amount * 10), Is.True);
            Assert.That(lathes.TryAddToQueue(machineUid, recipeProto, 2, lathe), Is.True);
            Assert.That(system.TrySelectBlueprint(library, second, "ClothingBackpackHolding"), Is.True);
            Assert.That(lathe.BlueprintRecipes["ClothingBackpackHolding"].InstanceId, Is.EqualTo("second"));
            Assert.That(lathes.GetAvailableRecipes(machineUid, lathe).Count(r => r.Id == "ClothingBackpackHolding"), Is.EqualTo(1));
            Assert.That(lathe.Queue.First!.Value.Modifiers!.InstanceId, Is.EqualTo("first"));
            Assert.That(lathes.TryStartProducing(machineUid, lathe), Is.True);
            Assert.That(lathe.CurrentBlueprintModifiers!.TimeMultiplier, Is.EqualTo(2));
            lathes.AbortProduction(machineUid, lathe);
            Assert.That(lathe.Queue.First!.Value.Modifiers!.TimeMultiplier, Is.EqualTo(2));
            containers.Remove(second, containers.GetContainer(library, "blueprint"));
            Assert.That(lathe.BlueprintRecipes, Is.Empty, "Ejecting the active source must withdraw its published recipe");
            Assert.That(lathe.Queue.First!.Value.Modifiers!.InstanceId, Is.EqualTo("first"));
            research.UnregisterClient(machineUid);
            Assert.That(lathe.BlueprintRecipes, Is.Empty);
            Assert.That(em.System<SharedUserInterfaceSystem>().TryGetUiState<BlueprintLibraryState>(library, BlueprintLibraryUiKey.Key, out var updated), Is.True);
            Assert.That(updated!.Blueprints.Single().Destinations["ClothingBackpackHolding"].Select(d => d.Entity), Is.EquivalentTo(new[] { em.GetNetEntity(secondMachine) }));
            var surgical = Blueprint("surgical", 1, "SurgicalKitMedical", "MedicalTechFab");
            Assert.That(system.TrySelectBlueprint(library, surgical, "SurgicalKitMedical"), Is.True);
            Assert.That(em.GetComponent<LatheComponent>(otherMachine).BlueprintRecipes.ContainsKey("SurgicalKitMedical"), Is.True);
            Assert.That(em.GetComponent<LatheComponent>(secondMachine).BlueprintRecipes.ContainsKey("SurgicalKitMedical"), Is.False);
            var weaponMachines = new[] { "MercenaryTechFab", "MercenaryTechFabHacked", "NfsdTechFab", "AmmoTechFab", "SecurityTechFab" }
                .Select(Spawn).ToArray();
            foreach (var client in weaponMachines)
            {
                research.UnregisterClient(client);
                research.RegisterClient(client, server);
            }
            var weaponTechnology = proto.Index<TechnologyBlueprintsPrototype>("NFAdvancedRiotControl");
            var weaponRecipe = weaponTechnology.Recipe.First();
            var weaponModifiers = new BlueprintModifiers
            {
                InstanceId = "weapon-compatible", Technology = weaponTechnology.ID, Lathe = weaponTechnology.Lathe,
                AdditionalLathes = new(weaponTechnology.AdditionalLathes), MaterialMultiplier = 2,
            };
            var weaponBlueprint = Spawn("ResearchRandomBlueprint");
            var weaponData = em.GetComponent<BlueprintComponent>(weaponBlueprint);
            system.SetBlueprint((weaponBlueprint, weaponData), [weaponRecipe], weaponModifiers);
            Assert.That(em.System<Content.Shared.Research.Systems.BlueprintSystem>().TryInsertBlueprint(
                (library, em.GetComponent<BlueprintReceiverComponent>(library)), (weaponBlueprint, weaponData), null), Is.True);
            Assert.That(system.TrySelectBlueprint(library, weaponBlueprint, weaponRecipe.Id), Is.True);
            Assert.That(em.System<SharedUserInterfaceSystem>().TryGetUiState<BlueprintLibraryState>(library, BlueprintLibraryUiKey.Key, out var weaponState), Is.True);
            Assert.That(weaponState!.Blueprints.Single(b => b.Entity == em.GetNetEntity(weaponBlueprint)).Destinations[weaponRecipe].Select(d => d.Entity),
                Is.EquivalentTo(weaponMachines.Take(3).Select(machine => em.GetNetEntity(machine))));
            foreach (var client in weaponMachines.Take(3))
            {
                var component = em.GetComponent<LatheComponent>(client);
                Assert.That(component.BlueprintRecipes[weaponRecipe].InstanceId, Is.EqualTo("weapon-compatible"));
                Assert.That(lathes.GetAvailableRecipes(client, component).Count(r => r == weaponRecipe), Is.EqualTo(1));
                foreach (var (material, amount) in lathes.GetMaterials(client, proto.Index(weaponRecipe), component))
                    Assert.That(materials.TryChangeMaterialAmount(client, material, amount * 2), Is.True);
                Assert.That(lathes.TryAddToQueue(client, proto.Index(weaponRecipe), 1, component), Is.True);
            }
            foreach (var client in weaponMachines.Skip(3).Append(otherMachine).Append(secondMachine))
                Assert.That(em.GetComponent<LatheComponent>(client).BlueprintRecipes.ContainsKey(weaponRecipe), Is.False);
            containers.Remove(weaponBlueprint, containers.GetContainer(library, "blueprint"));
            foreach (var client in weaponMachines.Take(3))
            {
                var component = em.GetComponent<LatheComponent>(client);
                Assert.That(component.BlueprintRecipes.ContainsKey(weaponRecipe), Is.False);
                Assert.That(component.Queue.First!.Value.Modifiers!.AdditionalLathes, Has.Count.EqualTo(2));
            }
            TestContext.Out.WriteLine("Blueprint research workflow assertions completed.");
        });
        try
        {
            await pair.Client.WaitAssertion(() =>
            {
                using var console = new ResearchBlueprintConsoleWindow();
                var result = new BlueprintResearchResult
                {
                    InstanceId = "console-test", Technology = "BluespaceStorage", Recipes = ["ClothingBackpackDuffelHolding"],
                    RareLoc = "research-technology-blueprint-uncommon",
                };
                console.Update(new ResearchBlueprintConsoleState(30000, 30000, true, [result]));
                var resultLabels = Descendants(console).OfType<RichTextLabel>().ToArray();
                var recipeName = pair.Client.EntMan.System<SharedLatheSystem>().GetRecipeName(pair.Client.ProtoMan.Index<LatheRecipePrototype>("ClothingBackpackDuffelHolding"));
                Assert.That(resultLabels.Any(label => label.GetFormattedMessage()?.ToString().Contains(recipeName) == true), Is.True);
                Assert.That(resultLabels.Any(label => label.GetFormattedMessage()?.ToString() == Loc.GetString(pair.Client.ProtoMan.Index<TechnologyBlueprintsPrototype>("BluespaceStorage").Name)), Is.False);
                var rarityLabel = resultLabels.Single(label => label.GetFormattedMessage()?.ToString() == Loc.GetString(result.RareLoc));
                Assert.That(rarityLabel.ActualModulateSelf, Is.EqualTo(Color.FromHex("#33FF44")));
                Assert.That(console.Roll.Disabled, Is.False);
                console.Update(new ResearchBlueprintConsoleState(0, 30000, true, [result]));
                Assert.That(console.Roll.Disabled, Is.True);
                Assert.That(Descendants(console).OfType<ProgressBar>().Single(c => c.Name == "BlueprintResearchFunding").Value, Is.Zero);
                console.Update(new ResearchBlueprintConsoleState(15000, 30000, true, [result]));
                Assert.That(Descendants(console).OfType<ProgressBar>().Single(c => c.Name == "BlueprintResearchFunding").Value, Is.EqualTo(0.5f));
                Assert.That(console.Roll.Disabled, Is.True);
                var history = pair.Client.ProtoMan.EnumeratePrototypes<TechnologyBlueprintsPrototype>()
                    .OrderByDescending(t => Loc.GetString(t.Name).Length).Take(30).Select(t => new BlueprintResearchResult
                    {
                        InstanceId = t.ID, Technology = t.ID, Recipes = t.Recipe.Concat(t.RecipeForce).ToList(), RareLoc = t.RareLoc,
                    }).ToList();
                console.Update(new ResearchBlueprintConsoleState(int.MaxValue, 30000, true, history));
                Assert.That(Descendants(console).OfType<ProgressBar>().Single(c => c.Name == "BlueprintResearchFunding").Value, Is.EqualTo(1));
                Assert.That(console.Roll.Disabled, Is.False);
                foreach (var size in new[] { new Vector2(520, 400), new Vector2(700, 560), new Vector2(900, 700) })
                {
                    console.SetSize = size;
                    console.Measure(size);
                    console.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    foreach (var text in Descendants(console).OfType<RichTextLabel>())
                    {
                        Assert.That(text.DesiredSize.X, Is.LessThanOrEqualTo(text.Size.X + 1));
                        Assert.That(text.DesiredSize.Y, Is.LessThanOrEqualTo(text.Size.Y + 1));
                    }
                }
                console.Update(new ResearchBlueprintConsoleState(0, 30000, false, new()));
                Assert.That(console.Roll.Disabled, Is.True);
                Assert.That(Descendants(console).OfType<ProgressBar>().Single(c => c.Name == "BlueprintResearchFunding").Value, Is.Zero);
                using var library = new BlueprintLibraryWindow();
                library.Update(windowState!);
                library.SetSize = new Vector2(1080, 620);
                library.Measure(new Vector2(1080, 620));
                library.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(1080, 620)));
                var lists = Descendants(library).OfType<BoxContainer>().ToArray();
                Assert.That(lists.Single(c => c.Name == "BlueprintItems").ChildCount, Is.EqualTo(1));
                Assert.That(lists.Single(c => c.Name == "BlueprintVariants").ChildCount, Is.EqualTo(2));
                Assert.That(Descendants(library).OfType<Button>().Single(c => c.Name == "BlueprintAssign").Disabled, Is.True);
                var texts = Descendants(library).OfType<RichTextLabel>().Select(l => l.GetFormattedMessage()?.ToString()).ToArray();
                Assert.That(texts.Any(t => t?.Contains(Loc.GetString("research-blueprint-variant-title", ("number", 1))) == true), Is.True);
                Assert.That(texts.Any(t => t?.Contains(Loc.GetString("research-blueprint-variant-title", ("number", 2))) == true), Is.True);
                Assert.That(texts.Any(t => t?.Contains("FIRST") == true || t?.Contains("SECOND") == true || t?.Contains("EngineeringTechFab") == true), Is.False);
                Assert.That(Descendants(library).Any(c => c.ToolTip?.Contains("EngineeringTechFab") == true || c.ToolTip?.Contains("first", StringComparison.OrdinalIgnoreCase) == true), Is.False);
                Assert.That(texts.Any(t => t?.Contains(windowState!.ServerName) == true), Is.True);
                Assert.That(Descendants(library).OfType<RichTextLabel>().Where(l => l.Name == "BlueprintTime")
                    .All(l => l.ActualModulateSelf == BlueprintLibraryWindow.ModifiedColor), Is.True);
                Assert.That(Descendants(library).OfType<RichTextLabel>().Where(l => l.Name == "BlueprintMaterial")
                    .All(l => l.ActualModulateSelf == BlueprintLibraryWindow.ModifiedColor), Is.True);
                var silver = pair.Client.ProtoMan.Index<MaterialPrototype>("Silver");
                for (var i = 0; i < 2; i++)
                {
                    var amount = i == 0 ? 16.0 : 23.5;
                    using var preview = new BlueprintLibraryWindow();
                    preview.Update(new BlueprintLibraryState([windowState!.Blueprints[i]], new(), true));
                    var materialTexts = Descendants(preview).OfType<RichTextLabel>().Select(l => l.GetFormattedMessage()?.ToString()).ToArray();
                    var silverQuantity = Loc.GetString("lathe-menu-material-amount", ("amount", amount), ("unit", Loc.GetString(silver.Unit)));
                    Assert.That(materialTexts.Any(t => t?.Contains(Loc.GetString("research-blueprint-material",
                        ("material", Loc.GetString(silver.Name)), ("amount", silverQuantity))) == true), Is.True);
                }

                var proto = pair.Client.ProtoMan;
                var latheSystem = pair.Client.EntMan.System<SharedLatheSystem>();
                var longRecipe = proto.EnumeratePrototypes<TechnologyBlueprintsPrototype>()
                    .SelectMany(t => t.Recipe.Concat(t.RecipeForce)).Distinct()
                    .OrderByDescending(r => latheSystem.GetRecipeName(r).Length).First();
                var longEntry = new BlueprintLibraryEntry(windowState!.Blueprints[0].Entity, [longRecipe], windowState.Blueprints[0].Modifiers.Copy());
                longEntry.Destinations[longRecipe] = windowState.Blueprints[0].Destinations["ClothingBackpackHolding"]
                    .Select(d => new BlueprintLibraryDestination(d.Entity, string.Join(" ", Enumerable.Repeat(d.Name, 5)), d.Prototype)).ToList();
                using var longWindow = new BlueprintLibraryWindow();
                longWindow.Update(new BlueprintLibraryState([longEntry], new(), true)
                {
                    ServerName = string.Join(" ", Enumerable.Repeat("Research Server", 8)), ServerId = 123,
                });
                Descendants(longWindow).OfType<RichTextLabel>().Single(c => c.Name == "BlueprintResourceFactor")
                    .SetMessage(FormattedMessage.FromUnformatted("Ресурсы ×2; дополнительных материалов: 1"));
                foreach (var size in new[] { new Vector2(750, 480), new Vector2(900, 620), new Vector2(950, 830), new Vector2(1440, 900) })
                {
                    longWindow.SetSize = size;
                    longWindow.Measure(size);
                    longWindow.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    var columns = Descendants(longWindow).Single(c => c.Name == "BlueprintColumns");
                    Assert.That(columns.ChildCount, Is.EqualTo(2));
                    Assert.That(columns.Children.Sum(c => c.Size.X) + 12, Is.EqualTo(columns.Size.X).Within(1));
                    var right = Descendants(longWindow).Single(c => c.Name == "BlueprintRightPane");
                    Assert.That(right.ChildCount, Is.EqualTo(2));
                    var variantsPane = Descendants(right).Single(c => c.Name == "BlueprintVariantsPane");
                    var craftPane = Descendants(right).Single(c => c.Name == "BlueprintCraftPane");
                    Assert.That(craftPane.Position.Y, Is.GreaterThanOrEqualTo(variantsPane.Position.Y + variantsPane.Size.Y));
                    var variantsContent = Descendants(variantsPane).Single(c => c.Name == "BlueprintVariants");
                    Assert.That(variantsContent.GetChild(0).Size.Y, Is.GreaterThanOrEqualTo(112));
                    Assert.That(variantsContent.DesiredSize.Y, Is.LessThanOrEqualTo(variantsContent.Parent!.Size.Y + 1),
                        "The only variant, including wrapped additional-resource text, must be fully visible without scrolling");
                    foreach (var text in Descendants(longWindow).OfType<RichTextLabel>())
                    {
                        Assert.That(text.Size.X, Is.LessThanOrEqualTo(text.Parent!.Size.X + 1));
                        Assert.That(text.DesiredSize.X, Is.LessThanOrEqualTo(text.Size.X + 1));
                        Assert.That(text.DesiredSize.Y, Is.LessThanOrEqualTo(text.Size.Y + 1));
                    }
                    var actions = Descendants(longWindow).Single(c => c.Name == "BlueprintActions");
                    Assert.That(actions.Size.Y, Is.GreaterThanOrEqualTo(36));
                    Assert.That(actions.GetChild(0).Position.X + actions.GetChild(0).Size.X, Is.LessThanOrEqualTo(actions.GetChild(1).Position.X));
                    Assert.That(actions.GetChild(0).Position.Y, Is.EqualTo(actions.GetChild(1).Position.Y));
                }
                var search = Descendants(longWindow).OfType<LineEdit>().Single();
                search.SetText("NoMatchingBlueprintForSearchTest", invokeEvent: true);
                Assert.That(Descendants(longWindow).Single(c => c.Name == "BlueprintItems").ChildCount, Is.Zero);
                Assert.That(Descendants(longWindow).Single(c => c.Name == "BlueprintActions").ChildCount, Is.Zero);
                search.SetText(string.Empty, invokeEvent: true);
                Assert.That(Descendants(longWindow).Single(c => c.Name == "BlueprintItems").ChildCount, Is.EqualTo(1));
                var changedMachine = new BlueprintLibraryEntry(windowState.Blueprints[0].Entity,
                    ["ClothingBackpackHolding"], new BlueprintModifiers
                    {
                        InstanceId = "highlight-test", Technology = "BluespaceStorage", Lathe = "MedicalTechFab",
                        ExtraMaterials = new() { ["Diamond"] = 25 },
                    });
                longWindow.Update(new BlueprintLibraryState([changedMachine], new(), true));
                var highlighted = Descendants(longWindow).OfType<RichTextLabel>().ToArray();
                Assert.That(highlighted.Where(l => l.Name == "BlueprintTime").All(l => l.ActualModulateSelf == Color.White), Is.True);
                Assert.That(highlighted.Where(l => l.Name == "BlueprintMachine").All(l => l.ActualModulateSelf == BlueprintLibraryWindow.ModifiedColor), Is.True);
                Assert.That(highlighted.Where(l => l.Name == "BlueprintMaterial").Count(l => l.ActualModulateSelf == BlueprintLibraryWindow.AdditionalMaterialColor), Is.EqualTo(1));
                Assert.That(highlighted.Where(l => l.Name == "BlueprintMaterial").Count(l => l.ActualModulateSelf == Color.White), Is.GreaterThan(0));
                longWindow.SetSize = new Vector2(950, 830);
                longWindow.Measure(new Vector2(950, 830));
                longWindow.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(950, 830)));
                var details = Descendants(longWindow).Single(c => c.Name == "BlueprintDetails");
                Assert.That(Descendants(details).Count(c => c.Name == "BlueprintMaterial"), Is.EqualTo(6));
                Assert.That(details.DesiredSize.Y, Is.LessThanOrEqualTo(details.Parent!.Size.Y), "All six materials and manufacturing conditions must fit without scrolling at the default size");
                var rightPane = Descendants(longWindow).Single(c => c.Name == "BlueprintRightPane");
                Assert.That(rightPane.GetChild(1).Size.Y, Is.GreaterThanOrEqualTo((rightPane.Size.Y - 12) * 0.60f));
                changedMachine.Modifiers.Lathe = "MercenaryTechFab";
                changedMachine.Modifiers.AdditionalLathes = ["MercenaryTechFabHacked", "NfsdTechFab"];
                longWindow.Update(new BlueprintLibraryState([changedMachine], new(), true));
                longWindow.Measure(new Vector2(950, 830));
                longWindow.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(950, 830)));
                var machineLabel = Descendants(longWindow).OfType<RichTextLabel>().Single(l => l.Name == "BlueprintMachine");
                var machineText = machineLabel.GetFormattedMessage()!.ToString();
                foreach (var target in changedMachine.Modifiers.GetLathes())
                {
                    Assert.That(machineText, Does.Contain(proto.Index(target).Name));
                    Assert.That(machineText, Does.Not.Contain(target.Id));
                }
                Assert.That(machineLabel.DesiredSize.X, Is.LessThanOrEqualTo(machineLabel.Size.X + 1));
                Assert.That(machineLabel.DesiredSize.Y, Is.LessThanOrEqualTo(machineLabel.Size.Y + 1));
                longWindow.Update(new BlueprintLibraryState(new(), new(), false));
                Assert.That(Descendants(longWindow).Single(c => c.Name == "BlueprintItems").ChildCount, Is.Zero);
                Assert.That(Descendants(longWindow).Single(c => c.Name == "BlueprintVariants").ChildCount, Is.Zero);
                Assert.That(Descendants(longWindow).Single(c => c.Name == "BlueprintActions").ChildCount, Is.Zero);
                TestContext.Out.WriteLine("Blueprint research UI assertions completed.");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Blueprint research UI failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }

    private static IEnumerable<Control> Descendants(Control root)
    {
        foreach (var child in root.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

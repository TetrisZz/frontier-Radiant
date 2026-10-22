using System.Linq;
using Content.Server._radiant.ResearchBlueprints;
using Content.Server.Lathe;
using Content.Server.Fax;
using Content.Server.Power.Components;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Fax;
using Content.Shared.Fax.Components;
using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using Content.Shared.Research.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests;

public sealed class ResearchBlueprintSpawningTest
{
    [TestPrototypes]
    private const string Prototypes = """
- type: blueprintResearch
  id: TestGuaranteedBlueprintModifiers
  parent: Default
  timeChance: 1
  materialChance: 1
  extraMaterialChance: 0
  latheChance: 0

- type: entity
  id: TestFixedRecipeBlueprint
  parent: NFBlueprintC20rSyndicate
  components:
  - type: Blueprint
    modifierPreset: TestGuaranteedBlueprintModifiers

- type: entity
  id: TestUnmodifiedBlueprint
  parent: TestFixedRecipeBlueprint
  components:
  - type: Blueprint
    randomizeModifiers: false
    rarity: research-technology-blueprint-common

- type: entity
  id: TestConfiguredBlueprint
  parent: TestFixedRecipeBlueprint
  components:
  - type: Blueprint
    modifiers:
      timeMultiplier: 4
      materialMultiplier: 3
      rareLoc: research-technology-blueprint-rare
""";

    [Test]
    public async Task FixedRecipesRandomModifiersOptOutAndCommonScrapPreset()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Fresh = true, Destructive = true, Connected = true,
        });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var proto = pair.Server.ProtoMan;
            var system = em.System<BlueprintResearchSystem>();
            Entity<BlueprintComponent> Spawn(string id)
            {
                var uid = em.SpawnEntity(id, map.GridCoords);
                return (uid, em.GetComponent<BlueprintComponent>(uid));
            }

            Assert.That(proto.Index<EntityPrototype>("NFBlueprintC20rSyndicate")
                .TryGetComponent<BlueprintComponent>(out var prototypeData, em.ComponentFactory), Is.True);
            var expected = prototypeData!.ProvidedRecipes;
            var first = Spawn("TestFixedRecipeBlueprint");
            var second = Spawn("TestFixedRecipeBlueprint");
            Assert.That(first.Comp.ProvidedRecipes, Is.EquivalentTo(expected));
            Assert.That(first.Comp.Modifiers, Is.Not.Null);
            var originalModifiers = first.Comp.Modifiers!;
            var snapshot = originalModifiers.Copy();
            Assert.That(snapshot.TimeMultiplier, Is.InRange(2, 7));
            Assert.That(snapshot.MaterialMultiplier, Is.InRange(2, 11));
            Assert.That(snapshot.RareLoc.Id, Is.EqualTo("research-technology-blueprint-unique"));
            Assert.That(snapshot.InstanceId, Is.Not.Empty.And.Not.EqualTo(second.Comp.Modifiers!.InstanceId));
            Assert.That(snapshot.Technology, Is.Null, "Supplying recipes does not invent a research technology.");
            system.InitializeBlueprint(first);
            var mapInit = new MapInitEvent();
            em.EventBus.RaiseLocalEvent(first.Owner, mapInit);
            Assert.That(first.Comp.Modifiers!.InstanceId, Is.EqualTo(snapshot.InstanceId));
            Assert.That(first.Comp.Modifiers.TimeMultiplier, Is.EqualTo(snapshot.TimeMultiplier));
            Assert.That(first.Comp.Modifiers.MaterialMultiplier, Is.EqualTo(snapshot.MaterialMultiplier));
            Assert.That(prototypeData.Modifiers, Is.Null,
                "Per-instance rolls must not modify shared prototypes.");

            var fixedBlueprint = Spawn("TestUnmodifiedBlueprint");
            Assert.That(fixedBlueprint.Comp.ProvidedRecipes, Is.EquivalentTo(expected));
            Assert.That(fixedBlueprint.Comp.Modifiers!.TimeMultiplier, Is.EqualTo(1));
            Assert.That(fixedBlueprint.Comp.Modifiers.MaterialMultiplier, Is.EqualTo(1));
            Assert.That(fixedBlueprint.Comp.Modifiers.ExtraMaterials, Is.Empty);
            Assert.That(fixedBlueprint.Comp.Modifiers.RareLoc.Id, Is.EqualTo("research-technology-blueprint-common"));
            var configured = Spawn("TestConfiguredBlueprint");
            Assert.That(configured.Comp.Modifiers!.TimeMultiplier, Is.EqualTo(4));
            Assert.That(configured.Comp.Modifiers.MaterialMultiplier, Is.EqualTo(3));
            Assert.That(configured.Comp.Modifiers.RareLoc.Id, Is.EqualTo("research-technology-blueprint-rare"));
            Assert.That(configured.Comp.Modifiers.InstanceId, Is.Not.Empty);

            var legacy = Spawn("NFBlueprintEngineeringRandom");
            Assert.That(legacy.Comp.ProvidedRecipes, Is.Not.Empty);
            Assert.That(legacy.Comp.Modifiers, Is.Not.Null, "Legacy recipe selection must precede modifier initialization.");

            var machine = em.SpawnEntity("MercenaryTechFab", map.GridCoords);
            var lathe = em.GetComponent<LatheComponent>(machine);
            var receiver = em.GetComponent<BlueprintReceiverComponent>(machine);
            Assert.That(em.System<BlueprintSystem>().TryInsertBlueprint((machine, receiver), first, null), Is.True);
            var latheSystem = em.System<LatheSystem>();
            foreach (var recipe in first.Comp.ProvidedRecipes)
                Assert.That(latheSystem.GetBlueprintModifiers(machine, proto.Index(recipe), lathe)!.InstanceId,
                    Is.EqualTo(snapshot.InstanceId));

            var fax = em.SpawnEntity("FaxMachineShip", map.GridCoords);
            em.GetComponent<ApcPowerReceiverComponent>(fax).Powered = true;
            Assert.That(em.System<ItemSlotsSystem>().TryInsert(fax, "Paper", second, null), Is.True);
            var faxSystem = em.System<FaxSystem>();
            faxSystem.Copy(fax, null, new FaxCopyMessage());
            var faxData = em.GetComponent<FaxMachineComponent>(fax);
            Assert.That(faxData.PrintingQueue.Single().BlueprintModifiers!.InstanceId,
                Is.EqualTo(second.Comp.Modifiers!.InstanceId));
            var beforeFax = em.EntityQuery<BlueprintComponent>().Select(c => c.Owner).ToHashSet();
            faxSystem.Update(0);
            faxSystem.Update(faxData.PrintingTime + 0.1f);
            var copied = em.EntityQuery<BlueprintComponent>().Single(c => !beforeFax.Contains(c.Owner));
            Assert.That(copied.ProvidedRecipes, Is.EquivalentTo(second.Comp.ProvidedRecipes));
            Assert.That(copied.Modifiers!.InstanceId, Is.EqualTo(second.Comp.Modifiers!.InstanceId));
            Assert.That(copied.Modifiers.TimeMultiplier, Is.EqualTo(second.Comp.Modifiers.TimeMultiplier));
            Assert.That(copied.Modifiers.MaterialMultiplier, Is.EqualTo(second.Comp.Modifiers.MaterialMultiplier));

            var common = proto.Index<BlueprintResearchPrototype>("ScrapProcessor");
            var defaultPreset = proto.Index<BlueprintResearchPrototype>("Default");
            Assert.That(common.MaterialTiers, Is.EquivalentTo(defaultPreset.MaterialTiers));
            Assert.That(common.DisciplineBlueprints, Is.EquivalentTo(defaultPreset.DisciplineBlueprints));
            Assert.That(common.AllowedRarities.Select(r => r.Id), Is.EqualTo(new[] { "research-technology-blueprint-common" }));
            foreach (var technology in proto.EnumeratePrototypes<TechnologyBlueprintsPrototype>().Where(defaultPreset.Allows))
            {
                Assert.That(defaultPreset.DisciplineBlueprints.ContainsKey(technology.Discipline), Is.True, technology.ID);
                var onlyThis = new BlueprintResearchPrototype { Tier = technology.Tier };
                Assert.That(onlyThis.Allows(technology), Is.True);
            }
            for (var i = 0; i < 64; i++)
            {
                Assert.That(system.TryCreateBlueprint(map.GridCoords, common, out var generated, out var technology), Is.True);
                Assert.That(generated.Comp.Modifiers!.RareLoc.Id, Is.EqualTo("research-technology-blueprint-common"));
                Assert.That(em.GetComponent<MetaDataComponent>(generated).EntityPrototype!.ID,
                    Is.EqualTo(common.BlueprintFor(technology).Id));
                Assert.That(technology.SupportsTier(common.Tier), Is.True);
            }

            var before = em.EntityQuery<BlueprintComponent>().Select(c => c.Owner).ToHashSet();
            em.SpawnEntity("SpawnScrapResearchBlueprint", map.GridCoords);
            var output = em.EntityQuery<BlueprintComponent>().Single(c => !before.Contains(c.Owner));
            Assert.That(output.Modifiers!.RareLoc.Id, Is.EqualTo("research-technology-blueprint-common"));
            TestContext.Out.WriteLine("Blueprint spawning, discipline colors, common scrap, opt-out and preserved recipes assertions completed.");
        });
        await pair.CleanReturnAsync();
    }
}

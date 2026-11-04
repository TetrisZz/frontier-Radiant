using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._HL.Shipyard;
using Content.Server._Triad.Shipyard;
using Content.Server.NPC.HTN;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Inventory;
using Content.Shared.Mind;
using Content.Shared.Silicons.Bots;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

[TestFixture]
public sealed class ShipSaveMedibotTest
{
    [Test]
    public async Task AutonomousMedibotSurvivesWithHatAndCanTreatAfterLoad()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.EntMan;
                var inventory = entities.System<InventorySystem>();
                var bot = entities.SpawnEntity("MobMedibot", map.GridCoords);
                var hat = entities.SpawnEntity("ClothingHeadHatTophat", map.GridCoords);
                Assert.That(inventory.TryEquip(bot, hat, "head", force: true), Is.True);
                var saves = entities.System<ShipyardGridSaveSystem>();
                Assert.That(saves.TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
                Assert.That(yaml, Does.Contain("MobMedibot"));
                entities.DeleteEntity(bot);

                using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml!, out _));
                Assert.That(entities.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader,
                    "saved medibot", out var loaded, offset: new Vector2(20, 20)), Is.True);
                var loadedBot = entities.EntityQuery<MedibotComponent, TransformComponent>()
                    .Single(entry => entry.Item2.GridUid == loaded.Value.Owner).Item2.Owner;
                Assert.That(entities.GetComponent<HTNComponent>(loadedBot).Enabled, Is.True);
                Assert.That(inventory.TryGetSlotEntity(loadedBot, "head", out var loadedHat), Is.True);
                Assert.That(entities.GetComponent<MetaDataComponent>(loadedHat.Value).EntityPrototype?.ID,
                    Is.EqualTo("ClothingHeadHatTophat"));
                var patient = entities.SpawnEntity("MobHuman", entities.GetComponent<TransformComponent>(loadedBot).Coordinates);
                Assert.That(entities.System<MedibotSystem>().TryInject(loadedBot, patient), Is.True);
                Assert.That(entities.System<SharedSolutionContainerSystem>()
                    .TryGetInjectableSolution(patient, out _, out var solution), Is.True);
                Assert.That(solution.GetTotalPrototypeQuantity("Tricordrazine"), Is.EqualTo(Content.Shared.FixedPoint.FixedPoint2.New(30)));

                // An occupied bot must not be copied with the ship or lose its controlling mind.
                var controlledBot = entities.SpawnEntity("MobMedibot", map.GridCoords);
                var minds = entities.System<SharedMindSystem>();
                var mind = minds.CreateMind(null);
                minds.TransferTo(mind, controlledBot);
                Assert.That(saves.TryBuildShipSaveYaml(map.Grid, out var controlledYaml, out _), Is.True);
                Assert.That(controlledYaml, Does.Not.Contain("MobMedibot"));
                Assert.That(minds.TryGetMind(controlledBot, out _, out _), Is.True);
                entities.DeleteEntity(loaded.Value.Owner);
            });
            await pair.CleanReturnAsync();
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Medibot ship save regression failure: {exception}");
            throw;
        }
    }
}

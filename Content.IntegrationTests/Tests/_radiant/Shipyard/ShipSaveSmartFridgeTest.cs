using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._Triad.Shipyard;
using Content.Server.Hands.Systems;
using Content.Shared._HL.Shipyard;
using Content.Shared.Interaction;
using Content.Shared.SmartFridge;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ShipSaveSmartFridgeTest
{
    [TestCase("SmartFridge")]
    [TestCase("SmartFridgeRadiant")]
    public async Task FullFridgeSavesWithoutMutatingLiveContentsAndLoadsEmpty(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var containers = em.System<SharedContainerSystem>();
                var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var fridge = em.SpawnEntity(prototype, map.GridCoords);
                if (!em.GetComponent<TransformComponent>(fridge).Anchored)
                    Assert.That(em.System<SharedTransformSystem>().AnchorEntity(fridge), Is.True);
                var component = em.GetComponent<SmartFridgeComponent>(fridge);
                foreach (var itemPrototype in new[] { "FoodApple", "FoodApple", "EmergencyMedipen" })
                {
                    var item = em.SpawnEntity(itemPrototype, map.GridCoords);
                    // Even explicitly persistent food must not bypass the fridge's empty-save policy.
                    em.AddComponent<HLPersistOnShipSaveComponent>(item);
                    Assert.That(em.System<HandsSystem>().TryPickupAnyHand(user, item), Is.True);
                    var insert = new InteractUsingEvent(user, item, fridge, map.GridCoords);
                    em.EventBus.RaiseLocalEvent(fridge, insert);
                    Assert.That(insert.Handled, Is.True);
                }
                var inventory = containers.GetContainer(fridge, component.Container);
                Assert.That(inventory.ContainedEntities.Count, Is.EqualTo(3));
                Assert.That(component.Entries.Count, Is.EqualTo(2));
                Assert.That(component.ContainedEntries.Values.Sum(items => items.Count), Is.EqualTo(3));

                Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
                Assert.That(yaml, Does.Not.Contain("FoodApple"));
                Assert.That(yaml, Does.Not.Contain("EmergencyMedipen"));
                Assert.That(inventory.ContainedEntities.Count, Is.EqualTo(3), "Building a save must not delete the live supplies.");
                Assert.That(component.Entries.Count, Is.EqualTo(2), "Export must not clear the live menu.");
                Assert.That(component.ContainedEntries.Values.Sum(items => items.Count), Is.EqualTo(3));

                // Empty before deleting to avoid unrelated active-container teardown handlers.
                containers.EmptyContainer(inventory, force: true);
                em.DeleteEntity(fridge);
                using var reader = new StringReader(yaml!);
                Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "empty saved smart fridge",
                    out var loaded, offset: new Vector2(20, 0)), Is.True);
                var restored = em.EntityQuery<SmartFridgeComponent, TransformComponent>(true)
                    .Single(e => e.Item2.GridUid == loaded!.Value.Owner).Item1;
                Assert.That(containers.GetContainer(restored.Owner, restored.Container).ContainedEntities, Is.Empty);
                Assert.That(restored.Entries, Is.Empty);
                Assert.That(restored.ContainedEntries, Is.Empty);

                em.System<SharedTransformSystem>().SetCoordinates(user, em.GetComponent<TransformComponent>(restored.Owner).Coordinates);
                var apple = em.SpawnEntity("FoodApple", em.GetComponent<TransformComponent>(user).Coordinates);
                Assert.That(em.System<HandsSystem>().TryPickupAnyHand(user, apple), Is.True);
                var newInsert = new InteractUsingEvent(user, apple, restored.Owner, em.GetComponent<TransformComponent>(apple).Coordinates);
                em.EventBus.RaiseLocalEvent(restored.Owner, newInsert);
                Assert.That(restored.Entries.Count, Is.EqualTo(1));
                em.EventBus.RaiseLocalEvent(restored.Owner,
                    new SmartFridgeDispenseItemMessage(restored.Entries.Single()) { Actor = user, UiKey = SmartFridgeUiKey.Key });
                Assert.That(containers.GetContainer(restored.Owner, restored.Container).ContainedEntities, Is.Empty);
                Assert.That(restored.Entries, Is.Empty);
                Assert.That(restored.ContainedEntries, Is.Empty, "A loaded fridge must accept and dispense new stock normally.");
                em.DeleteEntity(loaded!.Value.Owner);
            });
            Assert.That(pair.ServerLogHandler.FailingLogs, Is.Empty);
            await pair.CleanReturnAsync();
        }
        catch (System.Exception exception)
        {
            TestContext.Out.WriteLine(exception);
            throw;
        }
    }
}

using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._Triad.Shipyard;
using Content.Server.Stack;
using Content.Shared.Stacks;
using Content.Shared._HL.Shipyard;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ShipSaveRpedTest
{
    [Test]
    public async Task CivilianStashPreservesPartsAndStackCountsDirectlyAndInsideRped()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var storage = em.System<SharedStorageSystem>();
                var stash = em.SpawnEntity("CivilianStorageStash", map.GridCoords);
                var rped = em.SpawnEntity("RPED", map.GridCoords);
                var parts = new[] { "SuperCapacitorStockPart", "PicoManipulatorStockPartRandomVeryLow", "SuperMatterBinStockPartRandomVeryLow" };
                foreach (var prototype in parts)
                {
                    var part = em.SpawnEntity(prototype, map.GridCoords);
                    // Force a count outside the initial loot range to detect any reroll on load.
                    em.System<StackSystem>().SetCount(part, 7);
                    Assert.That(storage.Insert(rped, part, out _, playSound: false), Is.True);
                }
                const string directPart = "QuadraticCapacitorStockPartRandomVeryLow";
                var standalone = em.SpawnEntity(directPart, map.GridCoords);
                em.System<StackSystem>().SetCount(standalone, 6);
                Assert.That(storage.Insert(stash, standalone, out _, playSound: false), Is.True);

                var loose = em.SpawnEntity("FemtoManipulatorStockPartRandomVeryLow", map.GridCoords);
                Assert.That(em.GetComponent<TransformComponent>(loose).Anchored, Is.False);
                Assert.That(storage.Insert(stash, rped, out _, playSound: false), Is.True);
                // A legacy/custom persistence marker must never turn a full RPED into
                // an apparently successful empty snapshot while deleting the originals.
                em.AddComponent<HLPersistOnShipSaveComponent>(rped);
                Assert.Throws<InvalidDataException>(() => em.System<ShipyardGridSaveSystem>()
                    .TryBuildShipSaveYaml(map.Grid, out _, out _));
                Assert.That(em.GetComponent<StorageComponent>(rped).Container.ContainedEntities.Count, Is.EqualTo(3));
                em.RemoveComponent<HLPersistOnShipSaveComponent>(rped);
                Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
                Assert.That(yaml, Does.Not.Contain("FemtoManipulatorStockPartRandomVeryLow"));
                Assert.That(em.GetComponent<StorageComponent>(rped).Container.ContainedEntities.Count, Is.EqualTo(3),
                    "Saving must not modify live contents.");
                em.DeleteEntity(stash);
                using var reader = new StringReader(yaml!);
                Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "saved RPED", out var loaded,
                    offset: new Vector2(20, 0)), Is.True);
                var restored = em.EntityQuery<StorageComponent, MetaDataComponent, TransformComponent>(true)
                    .Single(e => e.Item2.EntityPrototype?.ID == "RPED" && e.Item3.GridUid == loaded!.Value.Owner).Item1;
                Assert.That(restored.Container.ContainedEntities.Select(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID),
                    Is.EquivalentTo(parts));
                foreach (var part in restored.Container.ContainedEntities)
                    Assert.That(em.GetComponent<StackComponent>(part).Count, Is.EqualTo(7));

                var loadedStash = em.EntityQuery<StorageComponent, MetaDataComponent, TransformComponent>(true)
                    .Single(e => e.Item2.EntityPrototype?.ID == "CivilianStorageStash" && e.Item3.GridUid == loaded!.Value.Owner).Item1;
                var loadedPart = loadedStash.Container.ContainedEntities
                    .Single(uid => em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == directPart);
                Assert.That(em.GetComponent<StackComponent>(loadedPart).Count, Is.EqualTo(6));
                em.DeleteEntity(loaded!.Value.Owner);
            });
            await pair.CleanReturnAsync();
        }
        catch (System.Exception exception)
        {
            TestContext.Out.WriteLine(exception);
            throw;
        }
    }
}

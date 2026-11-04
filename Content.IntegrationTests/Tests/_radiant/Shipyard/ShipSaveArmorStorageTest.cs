using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._Triad.Shipyard;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ShipSaveArmorStorageTest
{
    [Test]
    public async Task SamuraiArmorAndTacticalVestsSaveInCivilianAndMilitaryStashes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var storage = em.System<SharedStorageSystem>();
                var saves = em.System<ShipyardGridSaveSystem>();
                var armorPrototypes = new[]
                {
                    "ClothingOuterArmorTouseiGusoku", "ClothingOuterVestWeb", "ClothingOuterVestWebElite", "ClothingOuterVestWebMercenary"
                };
                foreach (var stashPrototype in new[] { "CivilianStorageStash", "MilitaryStorageStash" })
                {
                    var stash = em.SpawnEntity(stashPrototype, map.GridCoords);
                    foreach (var prototype in armorPrototypes)
                    {
                        var item = em.SpawnEntity(prototype, map.GridCoords);
                        var accepted = storage.Insert(stash, item, out _, playSound: false);
                        TestContext.Out.WriteLine($"ARMOR_STORAGE {stashPrototype}: {prototype}: accepted={accepted}");
                        Assert.That(accepted, Is.True, $"{prototype} must be valid in {stashPrototype}.");
                        if (em.HasComponent<StorageComponent>(item))
                        {
                            var magazine = em.SpawnEntity("NFMagazinePistol35", map.GridCoords);
                            Assert.That(storage.Insert(item, magazine, out _, playSound: false), Is.True);
                        }
                    }
                    Assert.That(saves.TryBuildShipSaveYaml(map.Grid.Owner, out var yaml, out _), Is.True, stashPrototype);
                    foreach (var prototype in armorPrototypes)
                        Assert.That(yaml, Does.Contain(prototype), $"{stashPrototype}: snapshot must retain {prototype}.");
                    using var reader = new StringReader(yaml!);
                    Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "armor storage regression",
                        out var loaded, offset: new Vector2(20, 0)), Is.True, stashPrototype);
                    var restored = em.EntityQuery<MetaDataComponent, TransformComponent>(true)
                        .Where(pair => pair.Item2.GridUid == loaded!.Value.Owner)
                        .Select(pair => pair.Item1.EntityPrototype?.ID).ToArray();
                    foreach (var prototype in armorPrototypes)
                        Assert.That(restored, Does.Contain(prototype), $"{stashPrototype}: loaded ship must contain {prototype}.");
                    Assert.That(restored.Count(id => id == "NFMagazinePistol35"), Is.EqualTo(3), "Vest contents must survive too.");
                    em.DeleteEntity(loaded!.Value.Owner);
                    em.DeleteEntity(stash);
                }
            });
            await pair.CleanReturnAsync();
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine(exception);
            throw;
        }
    }
}

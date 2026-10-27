using System;
using System.Linq;
using System.Numerics;
using Content.Server.Lathe;
using Content.Shared._radiant.Fishing.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Lathe;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class FishingEquipmentTest
{
    [Test]
    public async Task FishBagCollectsOnlyFishAndIsAvailableAtServiceTechfabs()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            EntityUid bag = default;
            EntityUid user = default;
            EntityUid quickFish = default;
            EntityUid areaFish = default;
            EntityUid rejected = default;
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var storage = em.System<SharedStorageSystem>();
                var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
                bag = em.SpawnEntity("FishBag", map.GridCoords);
                var contents = em.GetComponent<StorageComponent>(bag);
                Assert.That(contents.QuickInsert && contents.AreaInsert, Is.True);
                Assert.That(em.HasComponent<DumpableComponent>(bag), Is.True);
                foreach (var proto in prototypes.EnumeratePrototypes<EntityPrototype>()
                             .Where(proto => !proto.Abstract && proto.Components.ContainsKey("Fish")))
                {
                    var fish = em.SpawnEntity(proto.ID, map.GridCoords);
                    Assert.That(storage.Insert(bag, fish, out _, playSound: false), Is.True, proto.ID);
                    em.DeleteEntity(fish);
                }
                foreach (var id in new[] { "Crowbar", "FoodMeatFish", "RadiantFishingBaitNatural", "FishingRod" })
                {
                    var item = em.SpawnEntity(id, map.GridCoords);
                    Assert.That(storage.Insert(bag, item, out _, playSound: false), Is.False, id);
                    em.DeleteEntity(item);
                }
                foreach (var id in new[] { "ServiceTechFab", "McServiceTechFab" })
                {
                    var lathe = em.SpawnEntity(id, map.GridCoords.Offset(new Vector2(5, 0)));
                    var recipes = em.System<LatheSystem>().GetAvailableRecipes(lathe, em.GetComponent<LatheComponent>(lathe));
                    Assert.That(recipes.Exists(recipe => recipe.Id == "FishBag"), Is.True, id);
                }
                var crate = em.SpawnEntity("RadiantCrateFishingGear", map.GridCoords.Offset(new Vector2(5, 0)));
                Assert.That(em.GetComponent<EntityStorageComponent>(crate).Contents.ContainedEntities.Any(item =>
                    em.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == "FishBag"), Is.True);
                user = em.SpawnEntity("MobHuman", map.GridCoords);
                Assert.That(em.System<SharedHandsSystem>().TryPickup(user, bag), Is.True);
                quickFish = em.SpawnEntity("FishBass", map.GridCoords);
                var quick = new AfterInteractEvent(user, bag, quickFish, map.GridCoords, true);
                em.EventBus.RaiseLocalEvent(bag, quick);
                Assert.That(quick.Handled, Is.True);
                Assert.That(contents.Container.Contains(quickFish), Is.True);
            });
            await pair.RunTicksSync(30);
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                areaFish = em.SpawnEntity("FishTropical", map.GridCoords);
                rejected = em.SpawnEntity("Crowbar", map.GridCoords);
                var area = new AfterInteractEvent(user, bag, null, map.GridCoords, true);
                em.EventBus.RaiseLocalEvent(bag, area);
                Assert.That(area.Handled, Is.True);
            });
            await pair.RunTicksSync(90);
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var contents = em.GetComponent<StorageComponent>(bag);
                Assert.That(contents.Container.Contains(quickFish), Is.True);
                Assert.That(contents.Container.Contains(areaFish), Is.True);
                Assert.That(contents.Container.Contains(rejected), Is.False);
                Assert.That(contents.StoredItems, Has.Count.EqualTo(2));
            });
            await pair.Client.WaitAssertion(() =>
            {
                var em = pair.Client.EntMan;
                var resources = pair.Client.ResolveDependency<IResourceCache>();
                var mapUid = em.System<SharedMapSystem>().CreateMap(out _);
                try
                {
                    foreach (var (id, stateName) in new[]
                    {
                        ("FishingRod", "fishing_rod"), ("FishingRodClassic", "fishing_rod_telescopic"),
                        ("FishingRodMakeshift", "fishing_rod_bone"), ("FishingRodGolden", "fishing_rod_master"),
                        ("FishBag", "icon"),
                    })
                    {
                        var item = em.SpawnEntity(id, new EntityCoordinates(mapUid, 0, 0));
                        var sprite = em.GetComponent<SpriteComponent>(item);
                        Assert.That(sprite.BaseRSI, Is.Not.Null, id);
                        Assert.That(sprite.BaseRSI!.TryGetState(stateName, out _), Is.True, id);
                        if (em.HasComponent<FishingRodComponent>(item))
                        {
                            Assert.That(em.GetComponent<ItemComponent>(item).RsiPath,
                                Is.EqualTo("_radiant/Objects/Specific/Fishing/rod_inhands.rsi"));
                        }
                        em.DeleteEntity(item);
                    }
                    var hands = resources.GetResource<RSIResource>(new ResPath("/Textures/_radiant/Objects/Specific/Fishing/rod_inhands.rsi")).RSI;
                    foreach (var state in new[] { "inhand-left", "inhand-right" })
                        Assert.That(hands.TryGetState(state, out _), Is.True);
                    var belt = resources.GetResource<RSIResource>(new ResPath("/Textures/_radiant/Objects/Specific/Fishing/fish_bag.rsi")).RSI;
                    Assert.That(belt.TryGetState("equipped-BELT", out _), Is.True);
                }
                finally
                {
                    em.DeleteEntity(mapUid);
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}

using System;
using System.Linq;
using Content.Server.Lathe;
using Content.Server.Spawners.Components;
using Content.Server.Spawners.EntitySystems;
using Content.Shared._radiant.Nutrition;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Lathe;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Store;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Storage;

[TestFixture]
public sealed class RadiantTropicalItemsTest
{
    [Test]
    public async Task PalmsCoconutsCookiesAndPistol()
    {
        await using var pair = await PoolManager.GetServerClient();
        try
        {
            var map = await pair.CreateTestMap();
            EntityUid user = default;
            EntityUid coconut = default;
            EntityUid tool = default;
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                var palm = entities.SpawnEntity("FloraTreePalm", map.GridCoords);
                var timer = entities.GetComponent<TimedSpawnerComponent>(palm);
                Assert.That(timer.IntervalSeconds, Is.EqualTo(TimeSpan.FromMinutes(10)));
                Assert.That(timer.Prototypes.Single().Id, Is.EqualTo("DrinkCoconut"));
                Assert.That(timer.NextFire, Is.EqualTo(pair.Server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromMinutes(10)));
                entities.System<SpawnerSystem>().Update(0);
                Assert.That(entities.EntityQuery<CoconutDrinkComponent>().Count(), Is.Zero, "No coconut drops before the initial interval.");
                timer.NextFire = pair.Server.ResolveDependency<IGameTiming>().CurTime;
                entities.System<SpawnerSystem>().Update(0);
                Assert.That(entities.EntityQuery<CoconutDrinkComponent>().Count(), Is.EqualTo(1));
                Assert.That(timer.NextFire, Is.EqualTo(pair.Server.ResolveDependency<IGameTiming>().CurTime + TimeSpan.FromMinutes(10)));
                entities.System<SpawnerSystem>().Update(0);
                Assert.That(entities.EntityQuery<CoconutDrinkComponent>().Count(), Is.EqualTo(1));
                entities.DeleteEntity(palm);

                var storage = entities.System<SharedStorageSystem>();
                var box = entities.SpawnEntity("FoodBoxCookiesEmpty", map.GridCoords);
                foreach (var prototype in new[] { "Paper", "FoodBakedMuffin", "DrinkCoconut" })
                {
                    var item = entities.SpawnEntity(prototype, map.GridCoords);
                    Assert.That(storage.Insert(box, item, out _, playSound: false), Is.False, prototype);
                    entities.DeleteEntity(item);
                }
                foreach (var prototype in new[]
                         {
                             "FoodBakedCookie", "FoodBakedCookieOatmeal", "FoodBakedCookieRaisin",
                             "FoodBakedCookieSugar", "FoodBakedCookieDivine", "FoodSnackCookieFortune",
                             "FoodBakedDogTreatRS", "FoodBakedCookie",
                         })
                {
                    var cookie = entities.SpawnEntity(prototype, map.GridCoords);
                    Assert.That(storage.Insert(box, cookie, out _, playSound: false), Is.True, prototype);
                }
                var overflow = entities.SpawnEntity("FoodBakedCookie", map.GridCoords);
                Assert.That(storage.Insert(box, overflow, out _, playSound: false), Is.False);
                Assert.That(entities.GetComponent<StorageComponent>(box).StoredItems, Has.Count.EqualTo(8));
                Assert.That(entities.System<SharedAppearanceSystem>().TryGetData<int>(box,
                    StorageFillVisuals.FillLevel, out var level), Is.True);
                Assert.That(level, Is.EqualTo(8));

                foreach (var prototype in new[] { "ServiceTechFab", "McServiceTechFab" })
                {
                    var lathe = entities.SpawnEntity(prototype, map.GridCoords);
                    var recipes = entities.System<LatheSystem>().GetAvailableRecipes(lathe,
                        entities.GetComponent<LatheComponent>(lathe));
                    Assert.That(recipes.Exists(recipe => recipe.Id == "FoodBoxCookiesEmpty"), Is.True, prototype);
                }

                var pistol = entities.SpawnEntity("WeaponPistolVP78", map.GridCoords);
                var slots = entities.GetComponent<ItemSlotsComponent>(pistol);
                Assert.That(entities.GetComponent<MetaDataComponent>(slots.Slots["gun_magazine"].Item!.Value)
                    .EntityPrototype!.ID, Is.EqualTo("NFMagazinePistol35"));
                var slotSystem = entities.System<ItemSlotsSystem>();
                Assert.That(slotSystem.TryEject(pistol, "gun_magazine", null, out _), Is.True);
                var magazine = entities.SpawnEntity("NFMagazinePistol35", map.GridCoords);
                Assert.That(slotSystem.TryInsert(pistol, "gun_magazine", magazine, null), Is.True);
                var listing = pair.Server.ResolveDependency<IPrototypeManager>()
                    .Index<ListingPrototype>("UplinkHomWeaponPistolVP78");
                Assert.That(listing.ProductEntity, Is.EqualTo("WeaponPistolVP78"));

                coconut = entities.SpawnEntity("DrinkCoconut", map.GridCoords);
                user = entities.SpawnEntity("MobHuman", map.GridCoords);
                Assert.That(entities.System<OpenableSystem>().TryOpen(coconut), Is.False);
                tool = entities.SpawnEntity("Screwdriver", map.GridCoords);
                Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, tool), Is.True);
                var interaction = new InteractUsingEvent(user, tool, coconut, map.GridCoords);
                entities.EventBus.RaiseLocalEvent(coconut, interaction);
                Assert.That(interaction.Handled, Is.True);
            });

            await pair.RunTicksSync(180);
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                Assert.That(entities.GetComponent<CoconutDrinkComponent>(coconut).State,
                    Is.EqualTo(CoconutDrinkState.Carved));
                Assert.That(entities.System<OpenableSystem>().IsOpen(coconut), Is.True);
                Assert.That(entities.System<SharedSolutionContainerSystem>()
                    .TryGetSolution(coconut, "drink", out _, out var solution), Is.True);
                Assert.That(solution!.Volume.Int(), Is.EqualTo(50));
                entities.DeleteEntity(tool);
                tool = entities.SpawnEntity("Paper", map.GridCoords);
                Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, tool), Is.True);
                var interaction = new InteractUsingEvent(user, tool, coconut, map.GridCoords);
                entities.EventBus.RaiseLocalEvent(coconut, interaction);
                Assert.That(interaction.Handled, Is.True);
            });
            await pair.RunTicksSync(120);
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                Assert.That(entities.GetComponent<CoconutDrinkComponent>(coconut).State,
                    Is.EqualTo(CoconutDrinkState.CarvedStraw));
                Assert.That(entities.Deleted(tool), Is.True);
                Assert.That(entities.System<SharedSolutionContainerSystem>()
                    .TryGetSolution(coconut, "drink", out _, out var solution), Is.True);
                Assert.That(solution!.Volume.Int(), Is.EqualTo(50));
                coconut = entities.SpawnEntity("DrinkCoconut", map.GridCoords);
                tool = entities.SpawnEntity("KitchenKnife", map.GridCoords);
                Assert.That(entities.System<SharedHandsSystem>().TryPickup(user, tool), Is.True);
                var interaction = new InteractUsingEvent(user, tool, coconut, map.GridCoords);
                entities.EventBus.RaiseLocalEvent(coconut, interaction);
                Assert.That(interaction.Handled, Is.True);
            });
            await pair.RunTicksSync(180);
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                Assert.That(entities.GetComponent<CoconutDrinkComponent>(coconut).State,
                    Is.EqualTo(CoconutDrinkState.Chopped));
                Assert.That(entities.System<OpenableSystem>().IsOpen(coconut), Is.True);
                TestContext.Out.WriteLine("Radiant tropical items assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Radiant tropical items regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}

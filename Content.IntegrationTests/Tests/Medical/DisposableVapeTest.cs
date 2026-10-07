using Content.Server._radiant.Smoking;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Inventory;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class DisposableVapeTest
{
    [Test]
    public async Task WornVapeCanBeUsedRepeatedlyWithEitherActiveHand()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        var hands = entities.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
        EntityUid user = default;
        DisposableVapeComponent vape = default!;
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, 0, 0);
            user = entities.SpawnEntity("MobHuman", coordinates);
            // This isolated map has no atmosphere; keep the input test subject alive.
            entities.AddComponent<Content.Shared.Damage.Components.GodmodeComponent>(user);
            var item = entities.SpawnEntity("DisposableVapeBlue", coordinates);
            vape = entities.GetComponent<DisposableVapeComponent>(item);
            Assert.That(entities.System<InventorySystem>().TryEquip(user, item, "mask", force: true), Is.True);
        });

        for (var i = 0; i < 4; i++)
        {
            await server.WaitAssertion(() =>
            {
                var comp = entities.GetComponent<Content.Shared.Hands.Components.HandsComponent>(user);
                Assert.That(entities.System<Content.Shared.Mobs.Systems.MobStateSystem>().IsAlive(user), Is.True);
                Assert.That(vape.Cooldown, Is.Zero, "The normal server update must expire the cooldown.");
                hands.TrySetActiveHand((user, comp), comp.SortedHands[i % comp.SortedHands.Count]);
                Assert.That(hands.TryUseItemInHand(user), Is.True);
                Assert.That(vape.PuffsRemaining, Is.EqualTo(29 - i));
                hands.TryUseItemInHand(user);
                Assert.That(vape.PuffsRemaining, Is.EqualTo(29 - i), "Repeated input must not consume another puff.");
            });
            await server.WaitRunTicks(360);
        }
    }

    [Test]
    public async Task SealedVapeRequiresMaskAndStopsAfterThirtyPuffs()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, 0, 0);
            var human = entities.SpawnEntity("MobHuman", coordinates);
            var system = entities.System<DisposableVapeSystem>();
            var vape = entities.SpawnEntity("DisposableVapeBlue", coordinates);
            var comp = entities.GetComponent<DisposableVapeComponent>(vape);
            Assert.That(system.TryPuff(vape, human), Is.False);
            Assert.That(comp.PuffsRemaining, Is.EqualTo(30));
            Assert.That(entities.System<InventorySystem>().TryEquip(human, vape, "mask", force: true), Is.True);
            for (var i = 0; i < 30; i++)
            {
                Assert.That(system.TryPuff(vape, human), Is.True, $"Puff {i}");
                Assert.That(system.TryPuff(vape, human), Is.False, "Cooldown blocks repeated input.");
                system.Update(5);
                Assert.That(comp.PuffsRemaining, Is.EqualTo(29 - i), "No automatic puffs.");
            }
            Assert.That(comp.PuffsRemaining, Is.Zero);
            Assert.That(system.TryPuff(vape, human), Is.False);
            Assert.That(comp.Flavor, Is.EqualTo("disposable-vape-flavor-blueberry"));
            var solutions = entities.System<SharedSolutionContainerSystem>();
            var blood = entities.GetComponent<BloodstreamComponent>(human);
            Assert.That(solutions.TryGetSolution(human, blood.ChemicalSolutionName, out var solution), Is.True);
            Assert.That(solution!.Value.Comp.Solution.GetTotalPrototypeQuantity("Nicotine").Float(), Is.EqualTo(6).Within(.001));
            Assert.That(entities.HasComponent<Content.Shared.Chemistry.Components.RefillableSolutionComponent>(vape), Is.False);
            var user = entities.SpawnEntity("MobHuman", coordinates);
            var manual = entities.SpawnEntity("DisposableVapeGreen", coordinates);
            var manualComp = entities.GetComponent<DisposableVapeComponent>(manual);
            var inventory = entities.System<InventorySystem>();
            var hands = entities.System<Content.Shared.Hands.EntitySystems.SharedHandsSystem>();
            Assert.That(inventory.TryEquip(user, manual, "mask", force: true), Is.True);
            system.Update(100);
            Assert.That(manualComp.PuffsRemaining, Is.EqualTo(30), "Wearing never consumes puffs automatically.");
            Assert.That(hands.TryUseItemInHand(user), Is.True, "E falls back to the worn vape.");
            Assert.That(manualComp.PuffsRemaining, Is.EqualTo(29));
            hands.TryUseItemInHand(user);
            Assert.That(manualComp.PuffsRemaining, Is.EqualTo(29), "Repeated E respects cooldown.");
            system.Update(5);
            var pen = entities.SpawnEntity("Pen", coordinates);
            Assert.That(hands.TryPickupAnyHand(user, pen), Is.True);
            hands.TryUseItemInHand(user);
            Assert.That(manualComp.PuffsRemaining, Is.EqualTo(29), "An occupied hand takes priority.");
            Assert.That(hands.TryDrop(user, pen), Is.True);
            Assert.That(hands.TryUseItemInHand(user), Is.True);
            Assert.That(manualComp.PuffsRemaining, Is.EqualTo(28));

            var heldVape = entities.SpawnEntity("DisposableVapeRed", coordinates);
            Assert.That(hands.TryPickupAnyHand(user, heldVape), Is.True);
            var selfUse = new Content.Shared.Interaction.AfterInteractEvent(user, heldVape, user,
                entities.GetComponent<TransformComponent>(user).Coordinates, true);
            entities.EventBus.RaiseLocalEvent(heldVape, selfUse);
            Assert.That(entities.GetComponent<DisposableVapeComponent>(heldVape).PuffsRemaining, Is.EqualTo(29));
            Assert.That(manualComp.PuffsRemaining, Is.EqualTo(28), "Clicking uses the held vape, not the worn one.");
            var otherTarget = entities.SpawnEntity("MobHuman", coordinates);
            system.Update(5);
            var otherUse = new Content.Shared.Interaction.AfterInteractEvent(user, heldVape, otherTarget,
                entities.GetComponent<TransformComponent>(otherTarget).Coordinates, true);
            entities.EventBus.RaiseLocalEvent(heldVape, otherUse);
            Assert.That(entities.GetComponent<DisposableVapeComponent>(heldVape).PuffsRemaining, Is.EqualTo(29));
            Assert.That(manualComp.VaporMoles, Is.EqualTo(10f / 300f));
            foreach (var color in new[] { "Black", "Green", "Orange", "Purple", "Red", "White", "Yellow" })
            {
                var other = entities.SpawnEntity("DisposableVape" + color, coordinates);
                Assert.That(entities.GetComponent<DisposableVapeComponent>(other).PuffsRemaining, Is.EqualTo(30));
            }
            foreach (var id in new[]
            {
                "ClothingUniformRadiantBraskirt", "ClothingUniformRadiantReallyBlackSuit",
                "ClothingUniformRadiantBlackSuitFem", "ClothingUniformRadiantReallyBlackSuitSkirt",
                "ClothingUniformRadiantBlackSuitFemSkirt", "ClothingUniformRadiantParamedicDark",
                "ClothingUniformRadiantParamedicDarkSkirt", "ClothingUnderwearBottomRadiantStriped",
                "ClothingUnderwearBottomRadiantBee",
            })
                entities.SpawnEntity(id, coordinates);
        });
    }
}

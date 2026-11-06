using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._HL.Shipyard;
using Content.Server._Triad.Shipyard;
using Content.Server.Hands.Systems;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ShipSaveSurgicalCartTest
{
    [TestCase("RadiantSurgicalCart", 0)]
    [TestCase("RadiantSurgicalCartFilled", 8)]
    public async Task CartRestoresToolsAndSupportsWithdrawalAndDeconstruction(string prototype, int initialCount)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var em = pair.Server.EntMan;
            EntityUid cart = default, user = default, syringe = default;
            EntityUid[] tools = [];
            NetEntity netCart = default, netSyringe = default;
            await pair.Server.WaitAssertion(() =>
            {
                em.System<SharedMapSystem>().SetTile(map.Grid, new Vector2i(1, 0), new Tile(1));
                var original = em.SpawnEntity(prototype, map.GridCoords);
                if (initialCount == 0 && !em.GetComponent<TransformComponent>(original).Anchored)
                    Assert.That(em.System<SharedTransformSystem>().AnchorEntity(original), Is.True);
                Assert.That(em.GetComponent<StorageComponent>(original).Container.ContainedEntities, Has.Count.EqualTo(initialCount));
                Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);

                using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml!, out _));
                Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "saved surgical cart", out var loaded,
                    offset: new Vector2(20, 0)), Is.True);
                cart = em.EntityQuery<StorageComponent, MetaDataComponent, TransformComponent>(true)
                    .Single(e => e.Item2.EntityPrototype?.ID == prototype && e.Item3.GridUid == loaded!.Value.Owner).Item1.Owner;
                var storage = em.GetComponent<StorageComponent>(cart);
                Assert.That(storage.Container.ContainedEntities, Has.Count.EqualTo(initialCount));
                Assert.That(storage.StoredItems, Has.Count.EqualTo(initialCount));

                var cartCoords = em.GetComponent<TransformComponent>(cart).Coordinates;
                var userCoords = cartCoords.Offset(new Vector2(1, 0));
                user = em.SpawnEntity("MobHuman", userCoords);
                var players = pair.Server.ResolveDependency<IPlayerManager>();
                players.SetAttachedEntity(players.Sessions.Single(), user);
                if (initialCount == 0)
                {
                    var scalpel = em.SpawnEntity("Scalpel", userCoords);
                    Assert.That(em.System<SharedStorageSystem>().Insert(cart, scalpel, out _, playSound: false), Is.True);
                }
                syringe = em.SpawnEntity("Syringe", userCoords);
                Assert.That(em.System<HandsSystem>().TryPickupAnyHand(user, syringe), Is.True);
                Assert.That(em.System<SharedInteractionSystem>().InteractUsing(user, syringe, cart, cartCoords), Is.True);
                Assert.That(storage.Container.ContainedEntities, Does.Contain(syringe));
                Assert.That(storage.StoredItems.ContainsKey(syringe), Is.True);
                em.System<SharedStorageSystem>().OpenStorageUI(cart, user);
                netCart = em.GetNetEntity(cart);
                netSyringe = em.GetNetEntity(syringe);
            });
            await pair.RunTicksSync(15);
            await pair.Client.WaitAssertion(() =>
            {
                var client = pair.Client.EntMan;
                var storage = client.GetComponent<StorageComponent>(client.GetEntity(netCart));
                Assert.That(storage.StoredItems.ContainsKey(client.GetEntity(netSyringe)), Is.True);
                client.RaisePredictiveEvent(new StorageInteractWithItemEvent(netSyringe, netCart));
            });
            await pair.RunTicksSync(15);
            await pair.Server.WaitAssertion(() =>
            {
                var hands = em.System<HandsSystem>();
                Assert.That(hands.IsHolding(user, syringe), Is.True, "Clicking a tool in the restored cart must return it to the player.");
                var storage = em.GetComponent<StorageComponent>(cart);
                Assert.That(storage.Container.ContainedEntities, Does.Not.Contain(syringe));
                tools = storage.Container.ContainedEntities.ToArray();
                Assert.That(tools, Is.Not.Empty);
                Assert.That(hands.TryDrop(user, syringe), Is.True);
                var screwdriver = em.SpawnEntity("Screwdriver", em.GetComponent<TransformComponent>(user).Coordinates);
                Assert.That(hands.TryPickupAnyHand(user, screwdriver), Is.True);
                Assert.That(em.System<SharedInteractionSystem>().InteractUsing(user, screwdriver, cart,
                    em.GetComponent<TransformComponent>(cart).Coordinates), Is.True);
            });
            await pair.RunTicksSync(250);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(em.EntityExists(cart), Is.False);
                foreach (var tool in tools)
                {
                    Assert.That(em.EntityExists(tool), Is.True);
                    Assert.That(em.System<SharedContainerSystem>().IsEntityInContainer(tool), Is.False);
                    Assert.That(em.GetComponent<TransformComponent>(tool).MapID, Is.EqualTo(map.MapId));
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

using System.IO;
using System.Linq;
using System.Numerics;
using Content.Client.Alerts;
using Content.Client.Popups;
using Content.Server._HL.Shipyard;
using Content.Server._NF.Traits.Assorted;
using Content.Server._Triad.Shipyard;
using Content.Server._radiant.Plumbing;
using Content.Server.Hands.Systems;
using Content.Shared.Alert;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.SubFloor;
using Robust.Client.GameObjects;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ShipSavePlumbingTest
{
    [Test]
    public async Task PlumbedSinkDoesNotPredictAnEmptyLocalTank()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.EntMan;
        var client = pair.Client.EntMan;
        var user = EntityUid.Invalid;
        var sink = EntityUid.Invalid;
        var beaker = EntityUid.Invalid;
        var networkUser = NetEntity.Invalid;
        var networkSink = NetEntity.Invalid;
        var networkBeaker = NetEntity.Invalid;
        var tankCoords = map.GridCoords.Offset(Vector2.UnitX);
        var userCoords = map.GridCoords.Offset(-Vector2.UnitY);
        var solutions = server.System<SharedSolutionContainerSystem>();

        await pair.Server.WaitAssertion(() =>
        {
            var tiles = server.System<SharedMapSystem>();
            var plating = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            foreach (var coordinates in new[] { map.GridCoords, tankCoords, userCoords })
                tiles.SetTile(map.Grid, tiles.TileIndicesFor(map.Grid, coordinates), plating);

            sink = server.SpawnEntity("RadiantPlumbedSink", map.GridCoords);
            var tank = server.SpawnEntity("RadiantWaterSupplyTank", tankCoords);
            server.SpawnEntity("RadiantWaterPipe", map.GridCoords);
            server.SpawnEntity("RadiantWaterPipe", tankCoords);
            Assert.That(solutions.TryGetSolution(tank, "tank", out var water, out _), Is.True);
            solutions.AddSolution(water!.Value, new Solution("Water", 100));

            user = server.SpawnEntity("MobHuman", userCoords);
            beaker = server.SpawnEntity("Beaker", userCoords);
            var players = pair.Server.ResolveDependency<IPlayerManager>();
            players.SetAttachedEntity(players.Sessions.Single(), user);
            Assert.That(server.System<HandsSystem>().TryPickupAnyHand(user, beaker), Is.True);
            networkUser = server.GetNetEntity(user);
            networkSink = server.GetNetEntity(sink);
            networkBeaker = server.GetNetEntity(beaker);
        });
        await pair.RunTicksSync(12);
        await pair.Client.WaitAssertion(() =>
        {
            var target = client.GetEntity(networkSink);
            var coordinates = client.GetComponent<TransformComponent>(target).Coordinates;
            var interaction = client.System<SharedInteractionSystem>();
            var popups = client.System<PopupSystem>();
            var popupCount = popups.WorldLabels.Count;
            Assert.That(interaction.InteractUsing(client.GetEntity(networkUser), client.GetEntity(networkBeaker),
                target, coordinates), Is.False, "The server handles the faucet interaction.");
            Assert.That(popups.WorldLabels, Has.Count.EqualTo(popupCount));
        });
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(server.System<SharedInteractionSystem>().InteractUsing(user, beaker, sink, map.GridCoords), Is.True);
            Assert.That(solutions.TryGetRefillableSolution(beaker, out _, out var contents), Is.True);
            var amount = server.GetComponent<SolutionTransferComponent>(beaker).TransferAmount;
            Assert.That(contents!.GetTotalPrototypeQuantity("Water"), Is.EqualTo(amount));
            Assert.That(server.System<PlumbingSystem>().AvailableWater(sink), Is.EqualTo(100 - amount));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task PipeConnectorsFollowTheActualNetwork()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.EntMan;
        var client = pair.Client.EntMan;
        var straight = EntityUid.Invalid;
        var bend = EntityUid.Invalid;
        var water = NetEntity.Invalid;
        var waste = NetEntity.Invalid;
        var gas = NetEntity.Invalid;

        await pair.Server.WaitAssertion(() =>
        {
            var tiles = server.System<SharedMapSystem>();
            var plating = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
            var origin = tiles.TileIndicesFor(map.Grid, map.GridCoords);
            for (var y = -1; y <= 1; y++)
                tiles.SetTile(map.Grid, origin + new Vector2i(0, y), plating);

            straight = server.SpawnEntity("RadiantWaterPipeStraight", map.GridCoords);
            bend = server.SpawnEntity("RadiantWaterPipeBend", map.GridCoords.Offset(Vector2.UnitY));
            water = server.GetNetEntity(straight);
            waste = server.GetNetEntity(server.SpawnEntity("RadiantWastePipeStraight", map.GridCoords.Offset(-Vector2.UnitY)));
            gas = server.GetNetEntity(server.SpawnEntity("GasPipeStraight", map.GridCoords.Offset(-Vector2.UnitY)));
            var players = pair.Server.ResolveDependency<IPlayerManager>();
            players.SetAttachedEntity(players.Sessions.Single(), server.SpawnEntity("MobHuman", map.GridCoords));
        });
        await CheckConnectors(north: true, south: false);

        await pair.Server.WaitAssertion(() => server.System<SharedTransformSystem>().SetLocalRotation(straight, Angle.FromDegrees(180)));
        await CheckConnectors(north: false, south: true);

        await pair.Server.WaitAssertion(() => server.DeleteEntity(bend));
        await CheckConnectors(north: false, south: false);
        await pair.CleanReturnAsync();

        async Task CheckConnectors(bool north, bool south)
        {
            await pair.RunTicksSync(12);
            await pair.Client.WaitAssertion(() =>
            {
                Assert.That(ConnectorVisible(water, "NorthConnection0"), Is.EqualTo(north));
                Assert.That(ConnectorVisible(water, "SouthConnection0"), Is.EqualTo(south));
                Assert.That(ConnectorVisible(waste, "NorthConnection0"), Is.False);
                Assert.That(ConnectorVisible(gas, "NorthConnection0"), Is.False);
            });
        }

        bool ConnectorVisible(NetEntity entity, string key)
        {
            var uid = client.GetEntity(entity);
            var sprite = client.GetComponent<SpriteComponent>(uid);
            var sprites = client.System<SpriteSystem>();
            Assert.That(sprites.LayerMapTryGet((uid, sprite), key, out var layer, false), Is.True);
            return sprite[layer].Visible;
        }
    }

    [Test]
    public async Task FloorCoverAndHygieneAreVisibleToTheClient()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        var server = pair.Server.EntMan;
        var client = pair.Client.EntMan;
        var water = NetEntity.Invalid;
        var waste = NetEntity.Invalid;
        var player = EntityUid.Invalid;
        var observer = EntityUid.Invalid;
        var hygiene = server.System<HygieneSystem>();

        await pair.Server.WaitAssertion(() =>
        {
            SetTile("FloorSteel");
            water = server.GetNetEntity(server.SpawnEntity("RadiantWaterPipeStraight", map.GridCoords));
            waste = server.GetNetEntity(server.SpawnEntity("RadiantWastePipeBend", map.GridCoords));
            player = server.SpawnEntity("MobHuman", map.GridCoords);
            observer = server.SpawnEntity("MobHumanDummy", map.GridCoords);
            var players = pair.Server.ResolveDependency<IPlayerManager>();
            players.SetAttachedEntity(players.Sessions.Single(), player);
        });
        await CheckClient(visible: false, "RadiantHygiene");

        await pair.Server.WaitAssertion(() =>
        {
            SetTile("Plating");
            var dirt = server.GetComponent<HygieneComponent>(player);
            dirt.Dirt = 85;
            hygiene.OnPuddleSlip(player, new Solution("Blood", 1));
            Assert.That(dirt.NextOdorIncident, Is.Not.Null);
            Assert.That(Examine(observer), Does.Contain(StenchText()));
            Assert.That(Examine(player), Does.Not.Contain("%"));
            Assert.That(server.HasComponent<StinkyTraitComponent>(player), Is.False,
                "Dirt must not add a permanent trait that can be copied to a clone.");
        });
        await CheckClient(visible: true, "RadiantHygieneFilthy");

        await pair.Server.WaitAssertion(() =>
        {
            SetTile("FloorSteel");
            hygiene.Wash(player, 26);
            Assert.That(server.GetComponent<HygieneComponent>(player).NextOdorIncident, Is.Null);
            Assert.That(Examine(observer), Does.Not.Contain(StenchText()));
        });
        await CheckClient(visible: false, "RadiantHygieneDirty");

        await pair.Server.WaitAssertion(() => hygiene.Wash(player, 40));
        await CheckClient(visible: false, "RadiantHygieneSweaty");
        await pair.Server.WaitAssertion(() => hygiene.Wash(player, 10));
        await CheckClient(visible: false, "RadiantHygiene");
        await pair.Server.WaitAssertion(() => server.GetComponent<HygieneComponent>(player).Dirt = 80);
        await pair.RunTicksSync(130);
        await CheckClient(visible: false, "RadiantHygieneDirty");
        await pair.Server.WaitAssertion(() => server.GetComponent<HygieneComponent>(player).Dirt = 0);
        await pair.RunTicksSync(130);
        await CheckClient(visible: false, "RadiantHygiene");
        await pair.CleanReturnAsync();

        void SetTile(string id)
        {
            var tiles = server.System<SharedMapSystem>();
            var type = pair.Server.ResolveDependency<ITileDefinitionManager>()[id].TileId;
            var tile = tiles.TileIndicesFor(map.Grid, map.GridCoords);
            tiles.SetTile(map.Grid, tile, new Tile(type));
        }

        string Examine(EntityUid examiner)
        {
            var message = new ExaminedEvent(new FormattedMessage(), player, examiner, true, false);
            server.EventBus.RaiseLocalEvent(player, message);
            return message.GetTotalMessage().ToString();
        }

        string StenchText() => FormattedMessage.FromMarkupOrThrow(
            Loc.GetString("trait-stinky-examined", ("target", Identity.Entity(player, server)))).ToString();

        async Task CheckClient(bool visible, string stage)
        {
            await pair.RunTicksSync(12);
            await pair.Client.WaitAssertion(() =>
            {
                foreach (var pipe in new[] { water, waste })
                {
                    var rendered = client.TryGetEntity(pipe, out var uid) &&
                        client.TryGetComponent<SpriteComponent>(uid, out var sprite) && sprite.Visible &&
                        client.GetComponent<TransformComponent>(uid.Value).MapUid != null;
                    Assert.That(rendered, Is.EqualTo(visible));
                }

                var alerts = client.System<ClientAlertsSystem>().ActiveAlerts!;
                Assert.That(alerts[AlertKey.ForCategory("RadiantHygiene")].Type.Id, Is.EqualTo(stage));
            });
        }
    }

    [Test]
    public async Task PlumbingTransfersAndSurvivesShipReload()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var em = pair.Server.EntMan;
            var plumbing = em.System<PlumbingSystem>();
            var solutions = em.System<SharedSolutionContainerSystem>();
            var toggles = em.System<ItemToggleSystem>();
            var transform = em.System<SharedTransformSystem>();
            var horizontal = Angle.FromDegrees(90);
            string[] standaloneSinks = ["Sink", "SinkEmpty", "SinkWide", "SinkStemless", "SinkStemlessWater"];
            EntityUid supply = default, waste = default, sink = default, shower = default, oldShower = default, pipe = default, user = default;
            EntityCoordinates At(int x, int y = 0) => map.GridCoords.Offset(new Vector2(x, y));
            string? yaml = null;
            var spent = 0f;
            await pair.Server.WaitAssertion(() =>
            {
                var tiles = em.System<SharedMapSystem>();
                var plating = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["Plating"].TileId);
                for (var x = 0; x < 5; x++)
                for (var y = -3; y <= 1; y++)
                    tiles.SetTile(map.Grid, new Vector2i(x, y), plating);

                for (var x = 0; x < standaloneSinks.Length; x++)
                    em.SpawnEntity(standaloneSinks[x], At(x, -3));

                supply = em.SpawnEntity("RadiantWaterSupplyTank", At(0));
                waste = em.SpawnEntity("RadiantWastewaterTank", At(4));
                sink = em.SpawnEntity("RadiantPlumbedSink", At(2));
                shower = em.SpawnEntity("RadiantPlumbedShower", At(1));
                oldShower = em.SpawnEntity("RadiantShower", At(4, 1));
                user = em.SpawnEntity("MobHuman", At(2, -1));
                em.SpawnEntity("RadiantWaterPipe", At(0));
                pipe = em.SpawnEntity("RadiantWaterPipeStraight", At(1));
                em.SpawnEntity("RadiantWaterPipe", At(2));
                for (var x = 2; x <= 4; x++)
                    em.SpawnEntity("RadiantWastePipe", At(x));

                Assert.That(solutions.TryGetSolution(waste, "tank", out var dirty, out _), Is.True);
                solutions.AddSolution(dirty!.Value, new Solution("Water", 4998));
                transform.SetLocalRotation(pipe, horizontal);
            });
            await pair.Server.WaitRunTicks(5);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(toggles.TrySetActive(shower, true, predicted: false), Is.False, "The connected tank is empty.");
                Assert.That(toggles.TrySetActive(oldShower, true, predicted: false), Is.False, "The existing shower is disconnected.");
                Assert.That(solutions.TryGetSolution(supply, "tank", out var clean, out _), Is.True);
                solutions.AddSolution(clean!.Value, new Solution("Water", 100));
                transform.SetLocalRotation(pipe, Angle.Zero);
            });
            await pair.Server.WaitRunTicks(5);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(toggles.TrySetActive(shower, true, predicted: false), Is.False, "The openings do not face the tank.");
                Assert.That((float) plumbing.AvailableWater(sink), Is.Zero);
                transform.SetLocalRotation(pipe, horizontal);
                var floor = new Tile(pair.Server.ResolveDependency<ITileDefinitionManager>()["FloorSteel"].TileId);
                em.System<SharedMapSystem>().SetTile(map.Grid, new Vector2i(1, 0), floor);
                Assert.That(em.System<SharedAppearanceSystem>().TryGetData<bool>(pipe, SubFloorVisuals.Covered, out var covered), Is.True);
                Assert.That(covered, Is.True);
            });
            await pair.Server.WaitRunTicks(5);
            await pair.Server.WaitAssertion(() =>
            {
                var beaker = em.SpawnEntity("Beaker", At(2, -1));
                Assert.That(em.System<HandsSystem>().TryPickupAnyHand(user, beaker), Is.True);
                Assert.That(em.System<SharedInteractionSystem>().InteractUsing(user, beaker, sink, At(2)), Is.True);
                Assert.That(solutions.TryGetRefillableSolution(beaker, out var liquid, out var contents), Is.True);
                Assert.That((float) contents!.Volume, Is.GreaterThan(2));
                var firstFill = (float) contents.Volume;
                Assert.That(em.System<SharedInteractionSystem>().InteractUsing(user, beaker, sink, At(2)), Is.True);
                spent = (float) contents.Volume;
                Assert.That(spent, Is.GreaterThan(firstFill));
                Assert.That((float) plumbing.SendWaste(sink, liquid!.Value, contents.Volume), Is.EqualTo(2));
                Assert.That((float) contents.Volume, Is.EqualTo(spent - 2));
                Assert.That((float) plumbing.SendWaste(sink, liquid.Value, contents.Volume), Is.Zero);
                Assert.That(toggles.TrySetActive(shower, true, predicted: false), Is.True);
                em.DeleteEntity(pipe);
            });
            await pair.Server.WaitRunTicks(5);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That((float) plumbing.DrawWater(sink, 10), Is.Zero);
                Assert.That(em.GetComponent<ItemToggleComponent>(shower).Activated, Is.False);
                pipe = em.SpawnEntity("RadiantWaterPipeStraight", At(1));
                transform.SetLocalRotation(pipe, horizontal);
            });
            await pair.Server.WaitRunTicks(5);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(toggles.TrySetActive(shower, true, predicted: false), Is.True);
                Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid, out yaml, out _), Is.True);
            });
            EntityUid loadedSink = default, loadedShower = default, loadedWater = default, bather = default, loadedPipe = default;
            await pair.Server.WaitAssertion(() =>
            {
                using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml!, out _));
                Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "plumbing regression", out var loaded,
                    offset: new Vector2(20, 0)), Is.True);
                var fixtures = em.EntityQuery<MetaDataComponent, TransformComponent>(true)
                    .Where(ent => ent.Item2.GridUid == loaded!.Value.Owner)
                    .ToLookup(ent => ent.Item1.EntityPrototype?.ID, ent => ent.Item1.Owner);
                foreach (var standaloneSink in standaloneSinks)
                {
                    var loadedStandaloneSink = fixtures[standaloneSink].Single();
                    Assert.That(solutions.TryGetDrainableSolution(loadedStandaloneSink, out _, out _), Is.True, standaloneSink);
                }
                loadedWater = fixtures["RadiantWaterSupplyTank"].Single();
                var loadedWaste = fixtures["RadiantWastewaterTank"].Single();
                loadedSink = fixtures["RadiantPlumbedSink"].Single();
                Assert.That(solutions.TryGetSolution(loadedWater, "tank", out _, out var clean), Is.True);
                Assert.That(solutions.TryGetSolution(loadedWaste, "tank", out _, out var dirty), Is.True);
                Assert.That((float) clean!.Volume, Is.EqualTo(100 - spent));
                Assert.That((float) dirty!.Volume, Is.EqualTo(5000));
                loadedShower = fixtures["RadiantPlumbedShower"].Single();
                loadedPipe = fixtures["RadiantWaterPipe"].First();
                Assert.That(em.GetComponent<ItemToggleComponent>(loadedShower).Activated, Is.False);
            });
            await pair.Server.WaitRunTicks(5);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That((float) plumbing.DrawWater(loadedSink, 10), Is.EqualTo(10));
                var showerTransform = em.GetComponent<TransformComponent>(loadedShower);
                bather = em.SpawnEntity("MobHuman", showerTransform.Coordinates.Offset(showerTransform.LocalRotation.ToWorldVec()));
                var hygiene = em.GetComponent<HygieneComponent>(bather);
                hygiene.Dirt = 80;
                var hygieneSystem = em.System<HygieneSystem>();
                hygieneSystem.OnPuddleSlip(bather, new Solution("Water", 10));
                Assert.That(hygiene.Dirt, Is.EqualTo(80));
                hygieneSystem.OnPuddleSlip(bather, new Solution("Blood", 10));
                hygieneSystem.OnPuddleSlip(bather, new Solution("Blood", 10));
                Assert.That(hygiene.Dirt, Is.EqualTo(100));
                Assert.That(em.System<ItemToggleSystem>().TrySetActive(loadedShower, true, predicted: false), Is.True);
            });
            await pair.Server.WaitRunTicks(160);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(solutions.TryGetSolution(loadedWater, "tank", out _, out var remaining), Is.True);
                Assert.That((float) remaining!.Volume, Is.EqualTo(100 - spent - 10 - 7.5f));
                Assert.That(em.GetComponent<HygieneComponent>(bather).Dirt, Is.InRange(74f, 75f));
                Assert.That(em.System<ItemToggleSystem>().TrySetActive(loadedShower, false, predicted: false), Is.True);
                em.System<DamageableSystem>().SetAllDamage(loadedPipe, em.GetComponent<DamageableComponent>(loadedPipe), 15);
            });
            await pair.Server.WaitRunTicks(70);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(solutions.TryGetSolution(loadedWater, "tank", out _, out var remaining), Is.True);
                Assert.That((float) remaining!.Volume, Is.LessThan(100 - spent - 10 - 7.5f));
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

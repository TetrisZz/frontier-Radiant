using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._HL.Shipyard;
using Content.Server._NF.Station.Systems;
using Content.Server._Triad.Shipyard;
using Content.Server.DeviceNetwork.Systems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server.NodeContainer.Nodes;
using Content.Server.SurveillanceCamera;
using Content.Shared._NF.GridAccess;
using Content.Shared.Atmos;
using Content.Shared.Atmos.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Coordinates;
using Content.Shared.DeviceNetwork.Components;
using Content.Shared.Holopad;
using Content.Shared.Labels.Components;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.NodeContainer;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.SurveillanceCamera;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ShipSaveDevicePersistenceTest
{
    [Test]
    public async Task PipeNetworksKeepCompositionTemperatureAndQuantityAcrossRepeatedSaves()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid grid = map.Grid.Owner;
        await pair.Server.WaitAssertion(() =>
        {
            // Keep both isolated gas networks on one physically connected ship grid.
            for (var x = 0; x <= 4; x++)
                em.System<SharedMapSystem>().SetTile(map.Grid, new Vector2i(x, 0), new Tile(1));
            foreach (var x in new[] { 0, 1, 4 })
            {
                var pipe = em.SpawnEntity("GasPipeFourway", grid.ToCoordinates(x, 0));
                em.System<SharedTransformSystem>().AnchorEntity(pipe);
            }
            em.System<NodeGroupSystem>().ForceUpdate();
            var pipes = Pipes();
            Assert.That(pipes[0].NodeGroup, Is.SameAs(pipes[1].NodeGroup));
            Assert.That(pipes[0].NodeGroup, Is.Not.SameAs(pipes[2].NodeGroup));
            pipes[0].Air.SetMoles(Gas.Oxygen, 40);
            pipes[0].Air.SetMoles(Gas.Nitrogen, 10);
            pipes[0].Air.Temperature = 321;
            pipes[2].Air.SetMoles(Gas.Plasma, 7);
            pipes[2].Air.Temperature = 450;
        });
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(grid, out var yaml, out _), Is.True);
                CheckGas(); // Export must not remove gas from the live network.
                using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml!, out _));
                Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "pipe gas", out var loaded,
                    offset: new Vector2(20 * (cycle + 1), 0)), Is.True);
                em.DeleteEntity(grid);
                grid = loaded!.Value.Owner;
            });
            await pair.Server.WaitRunTicks(3);
            await pair.Server.WaitAssertion(() =>
            {
                CheckGas();
                TestContext.Out.WriteLine($"Pipe gas save/load cycle {cycle + 1} verified.");
            });
        }
        await pair.CleanReturnAsync();

        PipeNode[] Pipes() => em.EntityQuery<NodeContainerComponent, TransformComponent>(true)
            .Where(e => e.Item2.GridUid == grid).OrderBy(e => e.Item2.LocalPosition.X)
            .Select(e => (PipeNode)e.Item1.Nodes["pipe"]).ToArray();

        void CheckGas()
        {
            var pipes = Pipes();
            Assert.That(pipes, Has.Length.EqualTo(3));
            Assert.That(pipes[0].Air.GetMoles(Gas.Oxygen), Is.EqualTo(40).Within(0.001));
            Assert.That(pipes[0].Air.GetMoles(Gas.Nitrogen), Is.EqualTo(10).Within(0.001));
            Assert.That(pipes[0].Air.Temperature, Is.EqualTo(321).Within(0.01));
            Assert.That(pipes[2].Air.GetMoles(Gas.Plasma), Is.EqualTo(7).Within(0.001));
            Assert.That(pipes[2].Air.Temperature, Is.EqualTo(450).Within(0.01));
        }
    }

    [TestCase("RadiationCollector")]
    [TestCase("RadiationCollectorNoTank")]
    [TestCase("RadiationCollectorFullTank")]
    public async Task CollectorKeepsActualInstalledTankWithoutRefillingIt(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var slots = em.System<ItemSlotsSystem>();
            var collector = em.SpawnEntity(prototype, map.GridCoords);
            if (slots.TryEject(collector, "gas_tank", null, out var original))
                em.DeleteEntity(original);
            var tank = em.SpawnEntity("PlasmaTank", map.GridCoords);
            em.GetComponent<GasTankComponent>(tank).Air.SetMoles(Gas.Plasma, 12.5f);
            Assert.That(slots.TryInsert(collector, "gas_tank", tank, null), Is.True);
            Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
            using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml!, out _));
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "collector", out var loaded,
                offset: new Vector2(20, 0)), Is.True);
            var restored = em.EntityQuery<MetaDataComponent, TransformComponent>(true)
                .Single(e => e.Item2.GridUid == loaded!.Value.Owner && e.Item1.EntityPrototype?.ID == prototype).Item1.Owner;
            var contents = em.System<SharedContainerSystem>().GetContainer(restored, "gas_tank").ContainedEntities;
            Assert.That(contents, Has.Count.EqualTo(1));
            Assert.That(em.GetComponent<GasTankComponent>(contents.Single()).Air.GetMoles(Gas.Plasma), Is.EqualTo(12.5f));
            Assert.That(slots.TryEject(restored, "gas_tank", null, out _), Is.True);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ConstructedCameraSettingsAndManualHolopadLabelSurvive()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var camera = em.SpawnEntity("SurveillanceCameraConstructed", map.GridCoords);
            var user = em.SpawnEntity(null, map.GridCoords);
            em.EventBus.RaiseLocalEvent(camera, new SurveillanceCameraSetupSetName("Bridge")
                { Actor = user, UiKey = SurveillanceCameraSetupUiKey.Camera });
            em.EventBus.RaiseLocalEvent(camera, new SurveillanceCameraSetupSetNetwork(6)
                { Actor = user, UiKey = SurveillanceCameraSetupUiKey.Camera });
            var frequency = em.GetComponent<DeviceNetworkComponent>(camera).ReceiveFrequency;
            Assert.That(frequency, Is.Not.Null);
            var holopad = em.SpawnEntity("NFHolopadShip", map.GridCoords);
            var autoPad = em.SpawnEntity("NFHolopadShip", map.GridCoords);
            var rename = em.System<StationRenameHolopadsSystem>();
            rename.SyncHolopad((autoPad, em.GetComponent<HolopadComponent>(autoPad)), map.Grid.Owner);
            Assert.That(em.GetComponent<HolopadComponent>(autoPad).UseStationName, Is.True);
            em.System<LabelSystem>().Label(holopad, "My infirmary");
            Assert.That(em.GetComponent<HolopadComponent>(holopad).UseStationName, Is.False);
            Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
            Assert.That(em.GetComponent<DeviceNetworkComponent>(camera).ReceiveFrequency, Is.EqualTo(frequency));
            using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml!, out _));
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "camera and holopads", out var loaded,
                offset: new Vector2(20, 0)), Is.True);
            var restoredCamera = em.EntityQuery<SurveillanceCameraComponent, TransformComponent>(true)
                .Single(e => e.Item2.GridUid == loaded!.Value.Owner).Item1;
            Assert.That(restoredCamera.NameSet && restoredCamera.NetworkSet, Is.True);
            Assert.That(restoredCamera.CameraId, Is.EqualTo("Bridge"));
            Assert.That(em.GetComponent<DeviceNetworkComponent>(restoredCamera.Owner).ReceiveFrequency, Is.EqualTo(frequency));
            var pads = em.EntityQuery<HolopadComponent, TransformComponent>(true)
                .Where(e => e.Item2.GridUid == loaded!.Value.Owner).Select(e => e.Item1).ToArray();
            Assert.That(pads.Count(p => p.UseStationName), Is.EqualTo(1));
            var manual = pads.Single(p => !p.UseStationName);
            rename.SyncHolopad((manual.Owner, manual), loaded.Value.Owner);
            Assert.That(em.GetComponent<LabelComponent>(manual.Owner).CurrentLabel, Is.EqualTo("My infirmary"));
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task JanitorialSlotsAndNestedShipyardRemoteSurvive()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var slots = em.System<ItemSlotsSystem>();
            var containers = em.System<SharedContainerSystem>();
            var storage = em.System<SharedStorageSystem>();
            var cart = em.SpawnEntity("JanitorialTrolley", map.GridCoords);
            em.System<SharedTransformSystem>().AnchorEntity(cart);
            foreach (var (slot, proto) in new[] { ("mop_slot", "MopItem"), ("spraybottle_slot", "SprayBottleSpaceCleaner"), ("trashbag_slot", "TrashBag") })
                Assert.That(slots.TryInsert(cart, slot, em.SpawnEntity(proto, map.GridCoords), null), Is.True, slot);
            var stash = em.SpawnEntity("CivilianStorageStash", map.GridCoords);
            var backpack = em.SpawnEntity("ClothingBackpack", map.GridCoords);
            // Reproduce existing nested inventory independently of storage whitelists.
            Assert.That(containers.Insert(backpack, em.GetComponent<StorageComponent>(stash).Container, force: true), Is.True);
            var pouch = em.SpawnEntity("NFPouchUtility", map.GridCoords);
            Assert.That(storage.Insert(backpack, pouch, out _, playSound: false), Is.True);
            var remote = em.SpawnEntity("DoorRemoteShipyard", map.GridCoords);
            em.GetComponent<GridAccessComponent>(remote).LinkedShuttleUid = map.Grid.Owner;
            Assert.That(storage.CanInsert(stash, remote, out _), Is.True, "The shipyard remote is also allowed directly in the stash.");
            Assert.That(storage.Insert(pouch, remote, out _, playSound: false), Is.True);
            var banned = em.SpawnEntity("DoorRemoteCommand", map.GridCoords);
            Assert.That(storage.CanInsert(stash, banned, out _), Is.False);
            Assert.That(containers.Insert(banned, em.GetComponent<StorageComponent>(stash).Container, force: true), Is.True);
            Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
            using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml!, out _));
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "cleaning supplies and remote", out var loaded,
                offset: new Vector2(20, 0)), Is.True);
            var restored = em.EntityQuery<MetaDataComponent, TransformComponent>(true)
                .Where(e => e.Item2.GridUid == loaded!.Value.Owner).ToArray();
            Assert.That(restored.Any(e => e.Item1.EntityPrototype?.ID == "DoorRemoteCommand"), Is.False);
            var restoredCart = restored.Single(e => e.Item1.EntityPrototype?.ID == "JanitorialTrolley").Item1.Owner;
            foreach (var slot in new[] { "mop_slot", "spraybottle_slot", "trashbag_slot" })
                Assert.That(containers.GetContainer(restoredCart, slot).ContainedEntities, Has.Count.EqualTo(1), slot);
            var restoredRemote = restored.Single(e => e.Item1.EntityPrototype?.ID == "DoorRemoteShipyard").Item1.Owner;
            Assert.That(em.GetComponent<GridAccessComponent>(restoredRemote).LinkedShuttleUid, Is.EqualTo(loaded.Value.Owner));
            Assert.That(containers.TryGetContainingContainer(restoredRemote, out var remoteContainer), Is.True);
            Assert.That(em.GetComponent<MetaDataComponent>(remoteContainer!.Owner).EntityPrototype?.ID, Is.EqualTo("NFPouchUtility"));
        });
        await pair.CleanReturnAsync();
    }
}

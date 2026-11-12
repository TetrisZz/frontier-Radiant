using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._HL.Shipyard;
using Content.Server._NF.BindToStation;
using Content.Server._Triad.Shipyard;
using Content.Server.Construction.Components;
using Content.Server.Station.Systems;
using Content.Shared._NF.BindToStation;
using Content.Shared.Station;
using Content.Shared.Storage;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ShipSaveStationBindingTest
{
    [TestCase(true, false)]
    [TestCase(true, true)]
    [TestCase(false, true)]
    public async Task ForeignBindingBlocksSavingEvenInsideNestedContainers(bool enabled, bool nested)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var stations = em.System<StationSystem>();
            stations.InitializeNewStation(new StationConfig
            {
                StationPrototype = "StandardFrontierVessel", StationComponentOverrides = new()
            }, new[] { map.Grid.Owner });
            var foreignStation = em.SpawnEntity(null, map.GridCoords);
            var equipment = em.SpawnEntity("Screwdriver", map.GridCoords);
            em.System<BindToStationSystem>().BindToStation(equipment, foreignStation, enabled);
            if (nested)
            {
                var containers = em.System<SharedContainerSystem>();
                var storage = em.SpawnEntity("CivilianStorageStash", map.GridCoords);
                var bag = em.SpawnEntity("ClothingBackpack", map.GridCoords);
                Assert.That(containers.Insert(equipment, em.GetComponent<StorageComponent>(bag).Container), Is.True);
                // Simulate legacy contents or another insertion path bypassing the storage whitelist.
                Assert.That(containers.Insert(bag, em.GetComponent<StorageComponent>(storage).Container, force: true), Is.True);
            }

            var saving = em.System<ShipyardGridSaveSystem>();
            var reason = saving.GetShipSaveBlacklistReason(map.Grid.Owner);
            Assert.That(reason, Does.Contain(em.GetComponent<MetaDataComponent>(equipment).EntityName));
            Assert.That(saving.TryBuildShipSaveYaml(map.Grid.Owner, out var yaml, out _), Is.False);
            Assert.That(yaml, Is.Null);
            Assert.That(em.GetComponent<StationBoundObjectComponent>(equipment).BoundStation, Is.EqualTo(foreignStation));
            em.System<BindToStationSystem>().BindToStation(equipment, null, false);
            Assert.That(saving.GetShipSaveBlacklistReason(map.Grid.Owner), Is.Null);
        });
        await pair.CleanReturnAsync();
    }

    [TestCase(true)]
    [TestCase(false)]
    public async Task OwnBindingSurvivesReloadAndCannotBeSavedOnAnotherShip(bool enabled)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var stations = em.System<StationSystem>();
            var config = new StationConfig { StationPrototype = "StandardFrontierVessel", StationComponentOverrides = new() };
            var originalStation = stations.InitializeNewStation(config, new[] { map.Grid.Owner });
            var equipment = em.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
            Assert.That(em.System<SharedTransformSystem>().AnchorEntity(equipment), Is.True);
            em.System<BindToStationSystem>().BindToStation(equipment, originalStation, enabled);
            var saving = em.System<ShipyardGridSaveSystem>();
            Assert.That(saving.GetShipSaveBlacklistReason(map.Grid.Owner), Is.Null);
            Assert.That(saving.TryBuildShipSaveYaml(map.Grid.Owner, out var yaml, out _), Is.True);
            Assert.That(em.GetComponent<StationBoundObjectComponent>(equipment).BoundStation, Is.EqualTo(originalStation));
            using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml, out _));
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "station binding", out var loaded,
                offset: new Vector2(20, 20)), Is.True);
            var newStation = stations.InitializeNewStation(config, new[] { loaded.Value.Owner });
            saving.RestoreSavedStationBindings(loaded.Value.Owner, newStation);
            var query = em.EntityQueryEnumerator<StationBoundObjectComponent, TransformComponent>();
            EntityUid? restored = null;
            while (query.MoveNext(out var uid, out var binding, out var xform))
            {
                if (xform.GridUid != loaded.Value.Owner)
                    continue;
                Assert.That(binding.BoundStation, Is.EqualTo(newStation));
                Assert.That(binding.Enabled, Is.EqualTo(enabled));
                Assert.That(binding.SavedBindingEnabled, Is.Null);
                if (em.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "ResearchAndDevelopmentServer")
                    restored = uid;
            }
            Assert.That(restored, Is.Not.Null);
            Assert.That(saving.GetShipSaveBlacklistReason(loaded.Value.Owner), Is.Null);
            Assert.That(saving.TryBuildShipSaveYaml(loaded.Value.Owner, out _, out _), Is.True);

            em.System<SharedTransformSystem>().Unanchor(restored.Value);
            em.System<SharedTransformSystem>().SetCoordinates(restored.Value, map.GridCoords);
            Assert.That(saving.GetShipSaveBlacklistReason(map.Grid.Owner), Is.Not.Null);
            Assert.That(saving.TryBuildShipSaveYaml(map.Grid.Owner, out _, out _), Is.False);
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task ForeignBoardCannotBeLaunderedInsideAnOwnMachine()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var machine = em.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
            var binding = em.System<BindToStationSystem>();
            binding.BindToStation(machine, map.Grid.Owner);
            var board = em.GetComponent<MachineComponent>(machine).BoardContainer.ContainedEntities.Single();
            var foreign = em.SpawnEntity(null, map.GridCoords);
            binding.BindToStation(board, foreign);
            var saving = em.System<ShipyardGridSaveSystem>();
            Assert.That(saving.GetShipSaveBlacklistReason(map.Grid.Owner),
                Does.Contain(em.GetComponent<MetaDataComponent>(board).EntityName));
            Assert.That(saving.TryBuildShipSaveYaml(map.Grid.Owner, out _, out _), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}

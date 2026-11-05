using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._Triad.Shipyard;
using Content.Server.Botany.Components;
using Content.Server.Botany.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ShipSavePlantAnalyzerTest
{
    [TestCase("PlantAnalyzerEmpty")]
    [TestCase("PlantAnalyzer")]
    public async Task ScannedAnalyzerSavesAndLoadsWithoutRoundLocalScanData(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var stash = em.SpawnEntity("BotanicalStorageUnit", map.GridCoords);
            var analyzer = em.SpawnEntity(prototype, map.GridCoords);
            var seeds = em.SpawnEntity("WheatSeeds", map.GridCoords);
            var component = em.GetComponent<PlantAnalyzerComponent>(analyzer);
            component.Settings.AdvancedScan = true;
            var system = em.System<PlantAnalyzerSystem>();
            var state = system.GetPlantAnalyzerUiState((analyzer, component), seeds);
            Assert.That(state.PlantInfo, Is.Not.Null, "Reproduce a real populated scan cache.");
            Assert.That(state.PlantInfo!.Value.AdvancedInfo, Is.Not.Null);
            component.DoAfter = new DoAfterId(seeds, 12);
            Assert.That(em.System<SharedStorageSystem>().Insert(stash, analyzer, out _, playSound: false), Is.True);

            Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
            Assert.That(yaml, Does.Contain(prototype));
            Assert.That(component.PlantInfo, Is.Not.Null, "Export must not clear the live display.");
            Assert.That(component.DoAfter, Is.Not.Null, "Export must not cancel a live scan.");
            em.DeleteEntity(stash);
            using var reader = new StringReader(yaml!);
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "saved scanned plant analyzer",
                out var loaded, offset: new Vector2(20, 0)), Is.True);
            var restored = em.EntityQuery<PlantAnalyzerComponent, TransformComponent>(true)
                .Single(e => e.Item2.GridUid == loaded!.Value.Owner).Item1;
            Assert.That(restored.PlantInfo, Is.Null);
            Assert.That(restored.DoAfter, Is.Null);
            Assert.That(restored.Settings.AdvancedScan, Is.True, "Persistent scanner settings must survive.");
            Assert.That(restored.Settings.ScanDelay, Is.EqualTo(component.Settings.ScanDelay));
            Assert.That(restored.ScanningEndSound, Is.Not.Null);
            Assert.That(em.GetComponent<StorageComponent>(em.GetComponent<TransformComponent>(restored.Owner).ParentUid)
                .Container.ContainedEntities, Does.Contain(restored.Owner));
            Assert.That(system.GetPlantAnalyzerUiState((restored.Owner, restored), seeds).PlantInfo, Is.Not.Null,
                "The loaded analyzer must be able to produce a new scan.");
            em.DeleteEntity(loaded!.Value.Owner);
        });
        Assert.That(pair.ServerLogHandler.FailingLogs, Is.Empty);
        await pair.CleanReturnAsync();
    }
}

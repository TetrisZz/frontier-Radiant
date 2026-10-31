using System.Collections.Generic;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Maps;
using Content.Server.Shuttles.Components;
using Content.Server.Station.Systems;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ArtelShipyardTest
{
    [Test]
    public async Task ArtelLoadsItsWorkshopAndAppearsOnlyInTheGeneralShipyard()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var vessel = pair.Server.ProtoMan.Index<VesselPrototype>("Artel");
                Assert.That(vessel.Price, Is.EqualTo(40000));
                Assert.That(vessel.Name, Is.EqualTo("HOM Артель"));
                Assert.That(vessel.CanSave, Is.True);
                Assert.That(vessel.CareerProfession, Is.Null);
                var shipyard = em.System<ShipyardSystem>();
                var console = em.SpawnEntity("ComputerShipyard", map.GridCoords);
                Assert.That(shipyard.GetAvailableShuttles(console, ShipyardConsoleUiKey.Shipyard).available,
                    Does.Contain("Artel"));
                foreach (var prototype in new[] { "ComputerShipyardCareerMining", "ComputerShipyardCareerMedical",
                             "ComputerShipyardCareerEngineering", "ComputerShipyardCareers" })
                {
                    var professional = em.SpawnEntity(prototype, map.GridCoords);
                    Assert.That(shipyard.GetAvailableShuttles(professional, ShipyardConsoleUiKey.Custom).available,
                        Does.Not.Contain("Artel"));
                }

                Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, vessel.ShuttlePath, out var loaded), Is.True);
                var grid = loaded!.Value.Owner;
                Assert.That(em.HasComponent<ShuttleComponent>(grid), Is.True);
                var expected = new HashSet<string>
                {
                    "RadiantPotteryWheel", "RadiantArtisanKiln", "RadiantArtisanFurnace", "RadiantArtisanAnvil",
                    "RadiantQuenchingBath", "RadiantFermentationBarrel", "RadiantAutomaticBottler", "ServiceTechFab",
                };
                var installed = new HashSet<string>();
                var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out _, out var meta, out var xform))
                {
                    if (xform.GridUid == grid && meta.EntityPrototype is { } prototype)
                        installed.Add(prototype.ID);
                }
                Assert.That(installed.IsSupersetOf(expected), Is.True, "All four crafts must retain their mapped equipment.");
                var definition = pair.Server.ProtoMan.Index<GameMapPrototype>("Artel");
                var station = em.System<StationSystem>().InitializeNewStation(definition.Stations["Artel"], new List<EntityUid> { grid });
                Assert.That(em.GetComponent<MetaDataComponent>(station).EntityName,
                    Does.Match("^HOM Артель [A-Z]{2}-[0-9]{3}$"));
                em.DeleteEntity(grid);
            });
            Assert.That(pair.ServerLogHandler.FailingLogs, Is.Empty);
        }
        finally { await pair.CleanReturnAsync(); }
    }
}

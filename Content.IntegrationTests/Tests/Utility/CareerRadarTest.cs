using System;
using System.Collections.Generic;
using System.Linq;
using Content.Server._radiant.Careers;
using Content.Server.Maps;
using Content.Server.Station.Systems;
using Content.Shared._NF.Radar;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._radiant.Careers;
using Content.Shared.Shuttles.Components;
using Content.Shared.Shuttles.Systems;
using Robust.Server.GameStates;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class CareerRadarTest
{
    [Test]
    public async Task FleetRadarUsesNormalColorsAndKeepsNamesAndStealth()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var grids = new Dictionary<NetEntity, (string Profession, Color Color)>();
            foreach (var (profession, vesselId) in new[] { ("Mining", "Kestrel"), ("Engineering", "Retort"), ("Medical", "Eagle") })
            {
                var map = await pair.CreateTestMap();
                var extra = await pair.CreateTestMap();
                await pair.Server.WaitAssertion(() =>
                {
                    var em = pair.Server.EntMan;
                    var prototypes = pair.Server.ProtoMan;
                    var shuttles = em.System<SharedShuttleSystem>();
                    Assert.That(prototypes.Index<CareerProfessionPrototype>(profession).RadarColor, Is.Null);
                    var color = IFFComponent.IFFColor;

                    var metadata = em.System<MetaDataSystem>();
                    var stations = em.System<StationSystem>();
                    var station = stations.InitializeNewStation(prototypes.Index<GameMapPrototype>(vesselId).Stations.Values.Single(), new[] { map.Grid.Owner });
                    Assert.That(em.HasComponent<CareerRadarComponent>(map.Grid), Is.False);
                    stations.AddGridToStation(station, extra.Grid);
                    Assert.That(shuttles.GetIFFColor(extra.Grid), Is.EqualTo(color), "Grids added to the fleet inherit its presentation.");

                    var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                    var buyer = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                    em.System<CareerFleetSystem>().Register(station, map.Grid, buyer, session,
                        prototypes.Index<VesselPrototype>(vesselId), allowReplacement: false);
                    Assert.That(em.HasComponent<CareerRadarComponent>(map.Grid), Is.False, "Purchasing must not enable profession colors again.");

                    shuttles.SetIFFColor(map.Grid, Color.White);
                    color = Color.White;
                    Assert.That(shuttles.GetIFFColor(map.Grid), Is.EqualTo(color));
                    Assert.That(shuttles.GetIFFColor(map.Grid, self: true), Is.EqualTo(IFFComponent.SelfColor));
                    var name = profession + " " + vesselId + " AA-123";
                    metadata.SetEntityName(map.Grid, name);
                    Assert.That(shuttles.GetIFFLabel(map.Grid), Is.EqualTo(name), "Radar must retain the original profession prefix without adding a second label.");
                    Assert.That(em.GetComponent<MetaDataComponent>(map.Grid).EntityName, Is.EqualTo(name));
                    var renamed = profession + " Northern Light";
                    metadata.SetEntityName(map.Grid, renamed);
                    Assert.That(shuttles.GetIFFLabel(map.Grid), Is.EqualTo(renamed));
                    shuttles.AddIFFFlag(map.Grid, IFFFlags.HideLabel);
                    Assert.That(shuttles.GetIFFLabel(map.Grid), Is.Null);
                    Assert.That(shuttles.GetIFFLabel(map.Grid, self: true), Is.EqualTo(renamed));
                    shuttles.RemoveIFFFlag(map.Grid, IFFFlags.HideLabel);
                    shuttles.AddIFFFlag(map.Grid, IFFFlags.Hide);
                    Assert.That(shuttles.GetIFFLabel(map.Grid), Is.Null);
                    shuttles.RemoveIFFFlag(map.Grid, IFFFlags.Hide);
                    em.System<PvsOverrideSystem>().AddGlobalOverride(map.Grid);
                    // Old saved ships may still contain this marker; it must no longer change their color.
                    var legacy = em.EnsureComponent<CareerRadarComponent>(map.Grid);
                    legacy.Profession = profession;
                    em.Dirty(map.Grid, legacy);
                    Assert.That(shuttles.GetIFFColor(map.Grid), Is.EqualTo(color));
                    grids.Add(em.GetNetEntity(map.Grid), (profession, color));
                });
            }
            var ordinaryMap = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                em.System<MetaDataSystem>().SetEntityName(ordinaryMap.Grid, "Mining Pretender");
                var shuttles = em.System<SharedShuttleSystem>();
                shuttles.SetIFFColor(ordinaryMap.Grid, Color.White);
                Assert.That(shuttles.GetIFFColor(ordinaryMap.Grid), Is.EqualTo(Color.White));
                Assert.That(shuttles.GetIFFColor(ordinaryMap.Grid, self: true), Is.EqualTo(IFFComponent.SelfColor));
                Assert.That(shuttles.GetIFFLabel(ordinaryMap.Grid), Is.EqualTo("Mining Pretender"), "A name alone cannot impersonate a fleet.");
            });
            await pair.RunTicksSync(30);
            await pair.Client.WaitAssertion(() =>
            {
                var em = pair.Client.EntMan;
                foreach (var (net, expected) in grids)
                {
                    var grid = em.GetEntity(net);
                    Assert.That(em.GetComponent<CareerRadarComponent>(grid).Profession.Id, Is.EqualTo(expected.Profession));
                    Assert.That(em.System<SharedShuttleSystem>().GetIFFColor(grid), Is.EqualTo(expected.Color));
                    Assert.That(em.System<SharedShuttleSystem>().GetIFFLabel(grid), Is.EqualTo(expected.Profession + " Northern Light"));
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}

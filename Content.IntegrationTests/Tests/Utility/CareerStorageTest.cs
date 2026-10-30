using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Systems;
using Content.Server.Weapons.Ranged.Systems;
using Content.Server.Hands.Systems;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._NF.Weapons.Rarity;
using Content.Shared._radiant.Careers;
using Content.Shared._radiant.Shipyard;
using Content.Shared.Projectiles;
using Content.Shared.Roles;
using Content.Shared.Players.PlayTimeTracking;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Melee;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using YamlDotNet.RepresentationModel;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class CareerStorageTest
{
    [Test]
    public void CareerTerminalsArePlacedOnTheirWorkstationsWithoutChangingMapEntityIds()
    {
        var resources = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
        foreach (var (file, console, uid) in new[]
                 {
                     ("Maps/_radiant/Outpost/frontier_radiant.yml", "ComputerShipyardCareerEngineering", "3096"),
                     ("Maps/_NF/POI/medical.yml", "ComputerShipyardCareerMedical", "2732"),
                     ("Maps/_NF/Outpost/frontier.yml", "ComputerShipyardFrontier", "2120"),
                 })
        {
            var yaml = new YamlStream();
            using var reader = File.OpenText(Path.Combine(resources, file));
            yaml.Load(reader);
            var groups = (YamlSequenceNode)((YamlMappingNode)yaml.Documents.Single().RootNode).Children[new YamlScalarNode("entities")];
            var group = groups.Children.Cast<YamlMappingNode>()
                .Single(node => node.Children[new YamlScalarNode("proto")].ToString() == console);
            var entities = (YamlSequenceNode)group.Children[new YamlScalarNode("entities")];
            Assert.That(entities.Children.Cast<YamlMappingNode>()
                .Any(entity => entity.Children[new YamlScalarNode("uid")].ToString() == uid), Is.True, file);
        }
    }

    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: CareerStoredGunTest
  parent: NFWeaponPistolMk58
  components:
  - type: RareWeapon
    rarity: Epic
    accuracyModifier: 0.8
    fireRateModifier: 1.25
    projectileSpeedModifier: 1.4

- type: vessel
  id: CareerSaveAllowedTest
  parent: Hammer
  canSave: true

- type: vessel
  id: CareerSaveDefaultTest
  parent: BaseVessel
  shuttlePath: /Maps/_NF/Shuttles/hammer.yml

- type: vessel
  id: CareerSaveDeniedTest
  parent: Hammer
  canSave: false

- type: entity
  id: CareerHiringStationTest
  components:
  - type: StationJobs
    availableJobs:
      ContractorInterview: [0, 0]
";

    [Test]
    public async Task StoredGunRetainsModifiersAmmunitionAndCanActuallyFireAfterLoading()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var loader = em.System<MapLoaderSystem>();
                var guns = em.System<GunSystem>();
                var original = em.SpawnEntity("CareerStoredGunTest", map.GridCoords);
                var before = em.GetComponent<GunComponent>(original);
                var chamber = em.GetComponent<ChamberMagazineAmmoProviderComponent>(original);
                guns.SetBoltClosed(original, chamber, true);
                Assert.That(chamber.BoltClosed, Is.True, "Store a loaded pistol with its bolt closed.");
                var ammoBefore = new GetAmmoCountEvent();
                em.EventBus.RaiseLocalEvent(original, ref ammoBefore);
                Assert.That(ammoBefore.Count, Is.GreaterThan(0));
                var stats = (before.FireRateModified, before.ProjectileSpeedModified, before.MinAngleModified,
                    before.MaxAngleModified, before.AngleIncreaseModified, before.AngleDecayModified);
                using var writer = new StringWriter();
                Assert.That(loader.TrySaveEntity(original, writer), Is.True);
                em.DeleteEntity(original);
                using var reader = new StringReader(writer.ToString());
                Assert.That(loader.TryLoadEntity(reader, "personal safe regression", out var loaded), Is.True);
                var uid = loaded!.Value.Owner;
                var restored = em.GetComponent<GunComponent>(uid);
                Assert.That(em.GetComponent<ChamberMagazineAmmoProviderComponent>(uid).BoltClosed, Is.True,
                    "Loading must preserve the ready-to-fire bolt state.");
                Assert.That(restored.FireRateModified, Is.GreaterThan(0), "Derived stats must be rebuilt before the item leaves nullspace.");
                Assert.That((restored.FireRateModified, restored.ProjectileSpeedModified, restored.MinAngleModified,
                    restored.MaxAngleModified, restored.AngleIncreaseModified, restored.AngleDecayModified), Is.EqualTo(stats));
                var rare = em.GetComponent<RareWeaponComponent>(uid);
                Assert.That(rare.FireRateModifier, Is.EqualTo(1.25f));
                Assert.That(rare.ProjectileSpeedModifier, Is.EqualTo(1.4f));
                var ammoAfter = new GetAmmoCountEvent();
                em.EventBus.RaiseLocalEvent(uid, ref ammoAfter);
                Assert.That(ammoAfter.Count, Is.EqualTo(ammoBefore.Count));
                em.System<SharedTransformSystem>().SetCoordinates(uid, map.GridCoords);
                var shooter = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                Assert.That(em.System<HandsSystem>().TryPickupAnyHand(shooter, uid), Is.True);
                restored.NextFire = TimeSpan.Zero;
                restored.ShotCounter = 0;
                if (em.TryGetComponent<MeleeWeaponComponent>(uid, out var melee))
                    melee.NextAttack = TimeSpan.Zero;
                guns.AttemptShoot(shooter, uid, restored, map.GridCoords.Offset(new Vector2(10, 0)));
                Assert.That(em.EntityQuery<ProjectileComponent>().Count(p => p.Weapon == uid), Is.EqualTo(1));
                ammoAfter = new GetAmmoCountEvent();
                em.EventBus.RaiseLocalEvent(uid, ref ammoAfter);
                Assert.That(ammoAfter.Count, Is.EqualTo(ammoBefore.Count - 1));
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task HiringCareerShipyardsAndShipSavePermissionsUseExplicitPrototypes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var prototypes = pair.Server.ProtoMan;
                var shipyard = em.System<ShipyardSystem>();
                var hiring = em.System<GeneralStationRecordConsoleSystem>();
                var jobs = em.System<StationJobsSystem>();
                var station = em.SpawnEntity("CareerHiringStationTest", MapCoordinates.Nullspace);
                jobs.TrySetJobSlot(station, "ContractorInterview", 0, createSlot: true);
                foreach (var profession in new[] { "Mining", "Medical", "Engineering" })
                {
                    var career = prototypes.Index<CareerProfessionPrototype>(profession);
                    var interview = career.InterviewJob!.Value;
                    var interviewRole = prototypes.Index(interview);
                    Assert.That(interviewRole.PlayTimeTracker,
                        Is.Not.EqualTo(prototypes.Index<JobPrototype>(career.Jobs[0]).PlayTimeTracker));
                    Assert.That(prototypes.HasIndex<PlayTimeTrackerPrototype>(interviewRole.PlayTimeTracker), Is.True);
                    Assert.That(hiring.GetHiringJobs(station).ContainsKey(interview), Is.True);
                    Assert.That(CareerJobRules.IsAllowed(prototypes, profession, interview), Is.True);
                    Assert.That(CareerJobRules.IsAllowed(prototypes, "Adventurer", interview), Is.False);
                    var console = em.SpawnEntity($"ComputerShipyardCareer{profession}", MapCoordinates.Nullspace);
                    Assert.That(em.GetComponent<ShipyardConsoleComponent>(console).AllowShipSaving, Is.False);
                    var listing = shipyard.GetAvailableShuttles(console, ShipyardConsoleUiKey.Custom);
                    var offered = listing.available.Concat(listing.unavailable).ToArray();
                    Assert.That(offered, Has.Length.EqualTo(profession == "Mining" ? 5 : profession == "Medical" ? 6 : 7));
                    Assert.That(offered.All(id => prototypes.Index<VesselPrototype>(id).CareerProfession == profession), Is.True);
                    em.DeleteEntity(console);
                }
                var main = em.SpawnEntity("ComputerShipyardFrontier", MapCoordinates.Nullspace);
                Assert.That(em.GetComponent<ShipyardConsoleComponent>(main).AllowShipSaving, Is.True);
                foreach (var id in new[] { "ComputerShipyard", "ComputerShipyardSecurity", "ComputerShipyardSyndicate" })
                {
                    var console = em.SpawnEntity(id, MapCoordinates.Nullspace);
                    Assert.That(em.GetComponent<ShipyardConsoleComponent>(console).AllowShipSaving, Is.False, id);
                    em.DeleteEntity(console);
                }
                var grid = em.SpawnEntity(null, MapCoordinates.Nullspace);
                Assert.That(shipyard.CanSaveVessel(grid), Is.False);
                var saved = em.AddComponent<SavedShipComponent>(grid);
                saved.VesselId = "CareerSaveDefaultTest";
                Assert.That(shipyard.CanSaveVessel(grid), Is.False, "Unspecified canSave must default to false.");
                saved.VesselId = "CareerSaveAllowedTest";
                Assert.That(shipyard.CanSaveVessel(grid), Is.True);
                saved.VesselId = "CareerSaveDeniedTest";
                Assert.That(shipyard.CanSaveVessel(grid), Is.False, "A derived vessel must be able to disable an inherited permission.");
                foreach (var vesselId in new[] { "Hammer", "Prospector", "Haven", "Onix", "Bison" })
                {
                    saved.VesselId = vesselId;
                    Assert.That(shipyard.CanSaveVessel(grid), Is.True, vesselId);
                }
                foreach (var vessel in prototypes.EnumeratePrototypes<VesselPrototype>()
                             .Where(vessel => !vessel.Abstract && (vessel.CareerProfession != null
                                 || vessel.Group is not (ShipyardConsoleUiKey.Shipyard or ShipyardConsoleUiKey.Expedition or ShipyardConsoleUiKey.Scrap))))
                {
                    saved.VesselId = vessel.ID;
                    Assert.That(shipyard.CanSaveVessel(grid), Is.False, vessel.ID + ": service and antagonist vessels must remain restricted.");
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}

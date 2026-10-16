using System.Linq;
using Content.Server.Preferences.Managers;
using Content.Server.Power.Components;
using Content.Server.Maps;
using Content.Server.Station.Systems;
using Content.Server._radiant.Careers;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._radiant.Careers;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.GameTicking;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Console;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class CareerExpeditionTest
{
    [Test]
    public async Task ProgressDamageCapAndFiveMinuteSpawnGrace()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        var replacementMap = await pair.CreateTestMap();
        await pair.RunTicksSync(5);
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                var prefs = pair.Server.ResolveDependency<IServerPreferencesManager>().GetPreferences(session.UserId);
                var profile = (HumanoidCharacterProfile) prefs.SelectedCharacter;
                var previous = profile.Career;
                profile.Career = new CareerData { CharacterId = previous.CharacterId, Profession = "Mining" };
                var careers = entities.System<CareerSystem>();
                var player = entities.SpawnEntity("MobHumanDummy", map.GridCoords);
                var actor = entities.AddComponent<CareerActorComponent>(player);
                actor.User = session.UserId;
                actor.Slot = prefs.SelectedCharacterIndex;
                actor.CharacterId = profile.Career.CharacterId;
                actor.Job = "SalvageSpecialist";
                Assert.That(careers.IsWorking(player), Is.True);
                Assert.That(careers.IsJobAllowed(session, "Contractor"), Is.False);
                Assert.That(careers.IsJobAllowed(session, "SalvageSpecialist"), Is.True);
                Assert.That(careers.RecordActivity(player, CareerObjectiveType.DutyTime, 3600, [], []), Is.False,
                    "Adding duty time must fail when the current stage has no time objective");
                Assert.That(entities.System<SharedRoleSystem>()
                    .GetJobRequirement(new ProtoId<JobPrototype>("SalvageSpecialist")), Is.Null.Or.Empty,
                    "The entry-level career job must not require prior department playtime");
                careers.RecordActivity(player, CareerObjectiveType.SaleValue, 10000, [], ["Ore"], "Other");
                Assert.That(profile.Career.Level, Is.Zero, "Only the trade mall counts");
                careers.RecordActivity(player, CareerObjectiveType.SaleValue, 9999, [], ["Ore"], "Trade");
                Assert.That(profile.Career.Level, Is.Zero);
                careers.RecordActivity(player, CareerObjectiveType.SaleValue, 1, [], ["Ore"], "Trade");
                Assert.That(profile.Career.Level, Is.EqualTo(1));
                Assert.That(profile.Career.PendingRewards, Is.EqualTo(new[] { 1 }));
                Assert.That(profile.Career.Progress, Is.Empty, "New stage starts at zero");
                Assert.That(careers.RecordActivity(player, CareerObjectiveType.DutyTime, -60, [], []), Is.False);
                var commandPlayers = pair.Server.ResolveDependency<IPlayerManager>();
                var commandHost = pair.Server.ResolveDependency<IConsoleHost>();
                var previousAttached = session.AttachedEntity;
                commandPlayers.SetAttachedEntity(session, player);
                try
                {
                    commandHost.ExecuteCommand($"career_addtime \"{session.Name}\" 60");
                }
                finally
                {
                    commandPlayers.SetAttachedEntity(session, previousAttached);
                }
                Assert.That(profile.Career.Progress["duty"], Is.EqualTo(3600));
                Assert.That(profile.Career.Level, Is.EqualTo(1), "Time must not bypass the remaining ore objective");
                Assert.That(careers.RecordActivity(player, CareerObjectiveType.DutyTime, 3600, [], []), Is.False,
                    "Completed time objectives cannot accumulate extra time");
                careers.RecordActivity(player, CareerObjectiveType.SaleQuantity, 30, ["GoldOre"], [], "Trade");
                Assert.That(profile.Career.Level, Is.EqualTo(2));
                var target = entities.SpawnEntity("MobCarp", map.GridCoords);
                var damageable = entities.GetComponent<DamageableComponent>(target);
                var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
                var hit = new DamageSpecifier(prototypes.Index<DamageTypePrototype>("Blunt"), 10000);
                entities.EventBus.RaiseLocalEvent(target, new DamageChangedEvent(damageable, hit, true, player));
                var budget = entities.GetComponent<CareerDamageBudgetComponent>(target);
                Assert.That(budget.CreditedDamage, Is.EqualTo(budget.OriginalBudget));
                var credited = profile.Career.Progress["wildlife"];
                entities.EventBus.RaiseLocalEvent(target, new DamageChangedEvent(damageable, hit, true, player));
                Assert.That(profile.Career.Progress["wildlife"], Is.EqualTo(credited), "Reviving cannot replenish a target's credit");

                var station = entities.SpawnEntity(null, map.GridCoords);
                var fleet = entities.AddComponent<CareerFleetComponent>(station);
                fleet.Grid = map.Grid;
                var console = entities.SpawnEntity("ComputerTabletopStationRecords", map.GridCoords);
                var power = entities.GetComponent<ApcPowerReceiverComponent>(console);
                var now = pair.Server.ResolveDependency<IGameTiming>().CurTime;
                fleet.Participants[session.UserId] = player;
                power.Powered = false;
                fleet.UnpoweredSince = now - TimeSpan.FromSeconds(299);
                var fleets = entities.System<CareerFleetSystem>();
                Assert.That(fleets.IsSpawnEnabled(station), Is.True, "Power grace lasts five minutes");
                fleet.UnpoweredSince = now - TimeSpan.FromMinutes(5);
                Assert.That(fleets.IsSpawnEnabled(station), Is.False);
                power.Powered = true;
                Assert.That(fleets.IsSpawnEnabled(station), Is.True, "Restoring power re-enables spawning");
                fleet.Participants.Clear();
                fleet.Participants[new NetUserId(Guid.NewGuid())] = player;
                fleet.OfflineSince = now - TimeSpan.FromSeconds(299);
                Assert.That(fleets.IsSpawnEnabled(station), Is.True);
                fleet.OfflineSince = now - TimeSpan.FromMinutes(5);
                Assert.That(fleets.IsSpawnEnabled(station), Is.False);
                fleet.Participants[session.UserId] = player;
                Assert.That(fleets.IsSpawnEnabled(station), Is.True, "Any registered member returning resets offline grace");
                entities.DeleteEntity(station);
                var stations = entities.System<StationSystem>();
                var starterMap = prototypes.Index<GameMapPrototype>("CareerMiningStarter");
                var upgradeMap = prototypes.Index<GameMapPrototype>("CareerMiningUpgrade");
                var source = stations.InitializeNewStation(starterMap.Stations["CareerMiningStarter"], [map.Grid]);
                var destination = stations.InitializeNewStation(upgradeMap.Stations["CareerMiningUpgrade"], [replacementMap.Grid]);
                var players = pair.Server.ResolveDependency<IPlayerManager>();
                var attached = session.AttachedEntity;
                players.SetAttachedEntity(session, player);
                fleets.Register(source, map.Grid, player, session,
                    prototypes.Index<VesselPrototype>("CareerMiningStarter"));
                var upgrade = prototypes.Index<VesselPrototype>("CareerMiningUpgrade");
                Assert.That(fleets.GetFleetCount("Mining"), Is.EqualTo((1, 1)));
                Assert.That(fleets.CanPurchase(session, upgrade, out _), Is.True,
                    "An experienced crew member can replace the starter at the fleet cap");
                var cargo = entities.SpawnEntity("GoldOre", map.GridCoords);
                fleets.Register(destination, replacementMap.Grid, player, session, upgrade);
                Assert.That(entities.HasComponent<CareerFleetComponent>(source), Is.False);
                Assert.That(entities.GetComponent<CareerFleetComponent>(destination).Participants.ContainsKey(session.UserId), Is.True);
                Assert.That(fleets.IsSpawnEnabled(source), Is.False, "Old hull loses its crew registration");
                Assert.That(fleets.GetFleetCount("Mining").Active, Is.EqualTo(1));
                Assert.That(entities.EntityExists(cargo), Is.True, "Manual cargo transfer cannot delete old cargo");
                players.SetAttachedEntity(session, attached);
                entities.DeleteEntity(source);
                entities.DeleteEntity(destination);
                profile.Career = previous;
                entities.DeleteEntity(target);
                entities.DeleteEntity(player);
                entities.DeleteEntity(console);
                TestContext.Out.WriteLine("Career progression, target budget, crew/power grace and fleet replacement assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Career regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._radiant.Careers;
using Content.Server.Database;
using Content.Server.Preferences.Managers;
using Content.Server.Power.Components;
using Content.Server.Station.Systems;
using Content.Shared._EE.Contractors.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._radiant.Careers;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared._WF.SafetyDepositBox.Events;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.GameTicking;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Content.Shared.Storage;
using Content.Shared.UserInterface;
using Robust.Server.Player;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class CareerServiceRegressionTest
{
    [Test]
    public async Task EngineersAndTheirShipyardAreExclusiveToEachOther()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var players = pair.Server.ResolveDependency<IPlayerManager>();
                var session = players.Sessions.Single();
                var profile = (HumanoidCharacterProfile)pair.Server.ResolveDependency<IServerPreferencesManager>()
                    .GetPreferences(session.UserId).SelectedCharacter;
                var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                players.SetAttachedEntity(session, user);
                var fleet = em.System<CareerFleetSystem>();
                var consoles = new[] { "ComputerShipyardCareerEngineering", "ComputerShipyardCareers",
                    "ComputerShipyardCareerMining", "ComputerShipyardCareerMedical", "ComputerShipyard" }
                    .Select(id => (Id: id, Entity: em.SpawnEntity(id, map.GridCoords))).ToArray();
                foreach (var profession in new[] { "Engineering", "Mining", "Medical", "Adventurer" })
                {
                    profile.Career.Profession = profession;
                    foreach (var (id, console) in consoles)
                    {
                        var expected = profession == "Engineering"
                            ? id == "ComputerShipyardCareerEngineering"
                            : id != "ComputerShipyardCareerEngineering";
                        Assert.That(fleet.CanUseShipyard(session, console, out var reason), Is.EqualTo(expected),
                            $"{profession}: {id}");
                        Assert.That(reason == null, Is.EqualTo(expected));
                        em.GetComponent<ApcPowerReceiverComponent>(console).Powered = true;
                        var attempt = new ActivatableUIOpenAttemptEvent(user);
                        em.EventBus.RaiseLocalEvent(console, attempt);
                        Assert.That(attempt.Cancelled,
                            Is.EqualTo(profession != "Engineering" && id == "ComputerShipyardCareerEngineering"),
                            $"UI: {profession}: {id}");
                    }
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task RepeatedWithdrawRequestsIssueOneContainerWithItsStoredContents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var db = pair.Server.ResolveDependency<IServerDbManager>();
            EntityUid user = default;
            EntityUid terminal = default;
            Guid owner = default;
            var slot = 0;
            string paperData = "";
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                owner = session.UserId.UserId;
                slot = pair.Server.ResolveDependency<IServerPreferencesManager>().GetPreferences(session.UserId).SelectedCharacterIndex;
                user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                pair.Server.ResolveDependency<IPlayerManager>().SetAttachedEntity(session, user);
                terminal = em.SpawnEntity("SafetyDepositConsole", map.GridCoords);
                var paper = em.SpawnEntity("Paper", map.GridCoords);
                using var writer = new StringWriter();
                Assert.That(em.System<MapLoaderSystem>().TrySaveEntity(paper, writer), Is.True);
                paperData = writer.ToString();
                em.DeleteEntity(paper);
            });
            var record = await db.PurchaseSafetyDepositBox(owner, slot, "Storage test", "SafetyDepositBoxSmall");
            await db.DepositSafetyDepositBoxItems(record.BoxId, [paperData]);
            await pair.Server.WaitPost(() =>
            {
                for (var i = 0; i < 20; i++)
                    SendWithdraw();
            });
            var completed = false;
            for (var i = 0; i < 100; i++)
            {
                await pair.RunTicksSync(3);
                var stored = await db.GetSafetyDepositBox(record.BoxId);
                if (stored?.LastWithdrawn != null && stored.Items.Count == 0)
                {
                    completed = true;
                    break;
                }
            }
            Assert.That(completed, Is.True);
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                for (var i = 0; i < 20; i++)
                    SendWithdraw();
                var boxes = em.EntityQuery<SafetyDepositBoxComponent>().Where(box => box.BoxId == record.BoxId).ToArray();
                Assert.That(boxes, Has.Length.EqualTo(1));
                var storage = em.GetComponent<StorageComponent>(boxes[0].Owner);
                Assert.That(storage.Container.ContainedEntities, Has.Count.EqualTo(1));
                Assert.That(em.GetComponent<MetaDataComponent>(storage.Container.ContainedEntities.Single()).EntityPrototype!.ID, Is.EqualTo("Paper"));
            });
            await pair.RunTicksSync(10);
            await pair.Server.WaitAssertion(() => Assert.That(pair.Server.EntMan.EntityQuery<SafetyDepositBoxComponent>()
                .Count(box => box.BoxId == record.BoxId), Is.EqualTo(1), "A stale enabled button must not issue another empty container."));
            Assert.That(await db.GetPlayerSafetyDepositBoxes(owner, slot), Has.Count.EqualTo(1));

            await pair.Server.WaitPost(() =>
            {
                var em = pair.Server.EntMan;
                em.DeleteEntity(em.EntityQuery<SafetyDepositBoxComponent>().Single(box => box.BoxId == record.BoxId).Owner);
            });
            await db.ClearSafetyDepositBoxItems(record.BoxId, -1);
            await pair.Server.WaitPost(() =>
            {
                for (var i = 0; i < 20; i++)
                    pair.Server.EntMan.EventBus.RaiseLocalEvent(terminal,
                        new SafetyDepositReclaimMessage(record.BoxId) { Actor = user });
            });
            completed = false;
            for (var i = 0; i < 100; i++)
            {
                await pair.RunTicksSync(3);
                var stored = await db.GetSafetyDepositBox(record.BoxId);
                if (stored?.LastWithdrawnRoundId != null && stored.LastWithdrawnRoundId != -1)
                {
                    completed = true;
                    break;
                }
            }
            Assert.That(completed, Is.True, "Replacing a lost container must retain its existing database slot.");
            await pair.Server.WaitAssertion(() => Assert.That(pair.Server.EntMan.EntityQuery<SafetyDepositBoxComponent>()
                .Count(box => box.BoxId == record.BoxId), Is.EqualTo(1)));
            Assert.That(await db.GetPlayerSafetyDepositBoxes(owner, slot), Has.Count.EqualTo(1));

            void SendWithdraw() => pair.Server.EntMan.EventBus.RaiseLocalEvent(terminal,
                new SafetyDepositWithdrawMessage(record.BoxId) { Actor = user });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task MedicalCreditIsCappedByPatientIdentityAndStage()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                var prefs = pair.Server.ResolveDependency<IServerPreferencesManager>().GetPreferences(session.UserId);
                var profile = (HumanoidCharacterProfile) prefs.SelectedCharacter;
                profile.Career = new CareerData { CharacterId = profile.Career.CharacterId, Profession = "Medical" };
                var doctor = em.SpawnEntity("MobHuman", map.GridCoords);
                var actor = em.AddComponent<CareerActorComponent>(doctor);
                actor.User = session.UserId;
                actor.Slot = prefs.SelectedCharacterIndex;
                actor.CharacterId = profile.Career.CharacterId;
                actor.Job = "MedicalDoctor";
                Assert.That(em.System<CareerSystem>().IsWorking(doctor), Is.True);
                var patientId = Guid.NewGuid();
                var patient = NewPatient(patientId);
                var blunt = pair.Server.ProtoMan.Index<DamageTypePrototype>("Blunt");
                for (var i = 0; i < 6; i++)
                    Treat(patient);
                Assert.That(profile.Career.Progress["healing"], Is.EqualTo(200));
                Treat(doctor);
                Treat(NewPatient(patientId));
                Assert.That(profile.Career.Progress["healing"], Is.EqualTo(200), "A new body must not reset the patient's cap.");
                profile.Career = profile.Career.Clone();
                Treat(patient);
                Assert.That(profile.Career.Progress["healing"], Is.EqualTo(200), "Copying/reloading a profile must preserve the cap.");
                Treat(NewPatient(Guid.NewGuid()));
                Assert.That(profile.Career.Progress["healing"], Is.EqualTo(250));
                var medication = new CareerMedicationCreditComponent();
                medication.Reagents["Bicaridine"] = em.System<CareerMedicalSystem>().Capture(doctor)!.Value;
                em.AddComponent(patient, medication);
                var chemical = new CareerChemicalHealingEvent(patient, "Bicaridine", new DamageSpecifier(blunt, -50));
                em.EventBus.RaiseEvent(EventSource.Local, ref chemical);
                Assert.That(profile.Career.Progress["healing"], Is.EqualTo(250), "Chemical healing shares the same patient cap.");
                Assert.That(profile.Career.PatientHealing.Values.All(amount => amount <= 200), Is.True);

                EntityUid NewPatient(Guid id)
                {
                    var mob = em.SpawnEntity("MobHuman", map.GridCoords);
                    em.AddComponent<CareerActorComponent>(mob).CharacterId = id;
                    return mob;
                }
                void Treat(EntityUid target)
                {
                    var damage = em.System<DamageableSystem>();
                    damage.TryChangeDamage(target, new DamageSpecifier(blunt, 50), true);
                    damage.TryChangeDamage(target, new DamageSpecifier(blunt, -50), true, origin: doctor);
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task PassportExemptionsCareerSpawnPointsAndFleetListingsUseConfiguredRoles()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                var profile = new HumanoidCharacterProfile();
                foreach (var job in new[] { "SeparatistsCommander", "SeparatistsOperative", "SeparatistsWarden", "SeparatistsRecruit", "SeparatistsSlave", "SeparatistsPrivateSlave", "MedicalDoctorInterview" })
                {
                    var mob = em.SpawnEntity(job == "MedicalDoctorInterview" ? "InterviewHologramCareerMedical" : "MobHumanDummy", map.GridCoords);
                    var before = em.EntityQuery<PassportComponent>().Count();
                    em.EventBus.RaiseEvent(EventSource.Local,
                        new PlayerSpawnCompleteEvent(mob, session, job, true, false, 0, map.MapUid, profile));
                    Assert.That(em.EntityQuery<PassportComponent>().Count(), Is.EqualTo(before), job);
                    em.DeleteEntity(mob);
                }
                var ordinary = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var originalPassports = em.EntityQuery<PassportComponent>().Count();
                em.EventBus.RaiseEvent(EventSource.Local,
                    new PlayerSpawnCompleteEvent(ordinary, session, "MedicalDoctor", true, false, 0, map.MapUid, profile));
                Assert.That(em.EntityQuery<PassportComponent>().Count(), Is.EqualTo(originalPassports + 1));

                var row = 0;
                foreach (var (profession, job) in new[] { ("Medical", "MedicalDoctor"), ("Mining", "SalvageSpecialist"), ("Engineering", "StationEngineer") })
                {
                    var positions = new HashSet<Vector2>();
                    for (var i = 0; i < 3; i++)
                    {
                        var marker = em.SpawnEntity("SpawnPointCareer" + profession, map.GridCoords.Offset(new Vector2(i, row)));
                        positions.Add(em.GetComponent<TransformComponent>(marker).LocalPosition);
                    }
                    var spawnProfile = profile.Clone();
                    spawnProfile.Career.Profession = profession;
                    var spawn = new PlayerSpawningEvent(job, spawnProfile, null);
                    em.EventBus.RaiseEvent(EventSource.Local, spawn);
                    Assert.That(spawn.SpawnResult, Is.Not.Null, profession);
                    Assert.That(positions, Does.Contain(em.GetComponent<TransformComponent>(spawn.SpawnResult!.Value).LocalPosition), profession);
                    var definition = pair.Server.ProtoMan.Index<CareerProfessionPrototype>(profession);
                    for (var i = 0; i < definition.Levels.Count; i++)
                        Assert.That(pair.Server.ProtoMan.Index(definition.Levels[i]).RewardMoney, Is.EqualTo((i + 1) * 5000));
                    row++;
                }
                var combined = em.SpawnEntity("ComputerShipyardCareers", map.GridCoords);
                Assert.That(em.GetComponent<ShipyardListingComponent>(combined).Shuttles,
                    Is.EquivalentTo(new[] { "Kestrel", "Bulker", "Barge", "Hauler", "Gornyak", "CareerMedicalStarter", "Stasis", "Eagle", "Vitalis", "Salomandra", "Caladrius" }));
                foreach (var profession in new[] { "Mining", "Medical", "Engineering" })
                {
                    var console = em.SpawnEntity("ComputerShipyardCareer" + profession, map.GridCoords);
                    var expected = profession switch
                    {
                        "Mining" => new[] { "Kestrel", "Bulker", "Barge", "Hauler", "Gornyak" },
                        "Medical" => new[] { "CareerMedicalStarter", "Stasis", "Eagle", "Vitalis", "Salomandra", "Caladrius" },
                        _ => new[] { "CareerEngineeringStarter", "Retort", "Gasbender", "Ambition", "Hephaestus", "Comet", "Castor" },
                    };
                    Assert.That(em.GetComponent<ShipyardListingComponent>(console).Shuttles,
                        Is.EquivalentTo(expected));
                }
                var maps = em.System<SharedMapSystem>();
                maps.CreateMap(out var castorMap);
                try
                {
                    var castor = pair.Server.ProtoMan.Index<VesselPrototype>("Castor");
                    Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(castorMap, castor.ShuttlePath, out _), Is.True,
                        "The Castor shipyard entry must load its actual shuttle grid.");
                }
                finally { maps.DeleteMap(castorMap); }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}

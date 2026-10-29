using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading.Tasks;
using Content.Server.Chemistry.Components;
using Content.Server.Database;
using Content.Server.Maps;
using Content.Server.Power.Components;
using Content.Server.Research.Systems;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._Triad.Shipyard;
using Content.Server._radiant.Machines;
using Content.Server._radiant.ResearchBlueprints;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared.Access.Components;
using Content.Shared.Access;
using Content.Shared.Access.Systems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.FixedPoint;
using Content.Shared.GameTicking;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Research.Systems;
using Content.Shared.UserInterface;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class PersistentMachineTest
{
    [Test]
    public async Task MachinesRestoreDatabaseContentsAcrossRoundCleanupAndEnforceAccessAndUniqueKeys()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var em = pair.Server.EntMan;
            var libraryKey = "test-library-" + Guid.NewGuid().ToString("N");
            var chemicalKey = "test-chem-" + Guid.NewGuid().ToString("N");
            EntityUid library = default, chem = default, acorp = default, agent = default, visitor = default;
            var recipe = "";
            Task saveLibrary = Task.CompletedTask, saveChem = Task.CompletedTask;
            await pair.Server.WaitAssertion(() =>
            {
                library = Spawn("BlueprintLithographAcorpPersistent", libraryKey);
                chem = Spawn("ChemMasterPhoenixPersistent", chemicalKey);
                acorp = Actor("Acorp");
                agent = Actor("MedicalPhoenixAgent");
                visitor = Actor("MedicalPhoenix");
                Assert.That(OpenDenied(library, acorp), Is.True, "No access while database hydration is pending.");
            });
            await Loaded(library);
            await Loaded(chem);
            await pair.Server.WaitAssertion(() =>
            {
                foreach (var machine in new[] { library, chem }) em.GetComponent<ApcPowerReceiverComponent>(machine).Powered = true;
                Assert.That(OpenDenied(library, visitor), Is.True);
                Assert.That(OpenDenied(library, acorp), Is.False);
                Assert.That(OpenDenied(chem, visitor), Is.True, "A general medical-service tag is not support-agent access.");
                Assert.That(OpenDenied(chem, agent), Is.False);
                var duplicate = Spawn("BlueprintLithographAcorpPersistent", libraryKey);
                Assert.That(em.GetComponent<PersistentMachineComponent>(duplicate).Failed, Is.True);
                Assert.That(OpenDenied(duplicate, acorp), Is.True, "A second machine must not clone the same database inventory.");
                em.DeleteEntity(duplicate);

                var blueprint = em.SpawnEntity("ResearchRandomBlueprint", map.GridCoords);
                recipe = pair.Server.ProtoMan.EnumeratePrototypes<LatheRecipePrototype>().First().ID;
                em.System<BlueprintResearchSystem>().SetBlueprint((blueprint, em.GetComponent<BlueprintComponent>(blueprint)),
                    new HashSet<ProtoId<LatheRecipePrototype>> { recipe }, new BlueprintModifiers
                    {
                        InstanceId = "persistent-blueprint-instance", TimeMultiplier = 3, MaterialMultiplier = 2,
                        ExtraMaterials = new() { ["Steel"] = 2 }, Lathe = "Autolathe",
                    });
                var receiver = em.GetComponent<BlueprintReceiverComponent>(library);
                Assert.That(em.System<BlueprintSystem>().TryInsertBlueprint((library, receiver),
                    (blueprint, em.GetComponent<BlueprintComponent>(blueprint)), visitor), Is.False);
                Assert.That(em.System<BlueprintSystem>().TryInsertBlueprint((library, receiver),
                    (blueprint, em.GetComponent<BlueprintComponent>(blueprint)), acorp), Is.True);

                var solutions = em.System<SharedSolutionContainerSystem>();
                var beaker = em.SpawnEntity("LargeBeaker", map.GridCoords);
                Assert.That(solutions.TryGetFitsInDispenser(beaker, out var input, out var contents), Is.True);
                contents.MaxVolume = 5000;
                contents.CanReact = false;
                Assert.That(solutions.TryAddReagent(input!.Value, "Water", 1500), Is.True);
                Assert.That(solutions.TryAddReagent(input.Value, "Ethanol", 1500), Is.True);
                var bloodA = new ReagentId("Blood", new List<ReagentData> { new DnaData { DNA = "donor-a" } });
                var bloodB = new ReagentId("Blood", new List<ReagentData> { new DnaData { DNA = "donor-b" } });
                Assert.That(solutions.TryAddReagent(input.Value, bloodA, 700, out _), Is.True);
                Assert.That(solutions.TryAddReagent(input.Value, bloodB, 700, out _), Is.True);
                Assert.That(em.System<ItemSlotsSystem>().TryInsert(chem, SharedChemMaster.InputSlotName, beaker, agent), Is.True);
                Assert.That(em.System<ItemSlotsSystem>().GetItemOrNull(chem, SharedChemMaster.InputSlotName), Is.EqualTo(beaker));
                Assert.That(em.GetComponent<PersistentMachineComponent>(chem).Available, Is.True);
                Assert.That(em.System<AccessReaderSystem>().IsAllowed(agent, chem), Is.True);
                Assert.That(em.GetComponent<ChemMasterComponent>(chem).Mode, Is.EqualTo(ChemMasterMode.Transfer));
                Assert.That(contents.Volume, Is.EqualTo(FixedPoint2.New(4400)));
                Assert.That(contents.GetReagentQuantity(new ReagentId("Water", null)), Is.EqualTo(FixedPoint2.New(1500)));
                var discard = new ChemMasterSetModeMessage(ChemMasterMode.Discard) { Actor = agent, UiKey = ChemMasterUiKey.Key };
                em.EventBus.RaiseLocalEvent(chem, discard);
                Assert.That(em.GetComponent<ChemMasterComponent>(chem).Mode, Is.EqualTo(ChemMasterMode.Discard));
                var transfer = new ChemMasterSetModeMessage(ChemMasterMode.Transfer) { Actor = agent, UiKey = ChemMasterUiKey.Key };
                em.EventBus.RaiseLocalEvent(chem, transfer);
                var buffer = Buffer(chem);
                Transfer(chem, visitor, new ReagentId("Water", null));
                Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.Zero), "Messages from an unauthorized actor must not mutate the buffer.");
                foreach (var reagent in new[] { new ReagentId("Water", null), new ReagentId("Ethanol", null), bloodA, bloodB })
                    Transfer(chem, agent, reagent);
                Assert.That(Buffer(chem).GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(1000)),
                    $"Input water: {contents.GetTotalPrototypeQuantity("Water")}; mode: {em.GetComponent<ChemMasterComponent>(chem).Mode}; buffer capacity: {buffer.MaxVolume}");
                Assert.That(buffer.GetTotalPrototypeQuantity("Ethanol"), Is.EqualTo(FixedPoint2.New(1000)));
                Assert.That(buffer.GetTotalPrototypeQuantity("Blood"), Is.EqualTo(FixedPoint2.New(1000)), "Different DNA variants share one reagent-type limit.");
                Assert.That(contents.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(500)));
                Assert.That(contents.GetTotalPrototypeQuantity("Blood"), Is.EqualTo(FixedPoint2.New(400)));
                Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.New(3000)), "The limit is per reagent, not per whole mixture.");
                var ordinary = em.SpawnEntity("ChemMaster", map.GridCoords);
                var ordinaryBeaker = em.SpawnEntity("LargeBeaker", map.GridCoords);
                Assert.That(solutions.TryGetFitsInDispenser(ordinaryBeaker, out var ordinaryInput, out var ordinaryContents), Is.True);
                ordinaryContents.MaxVolume = 2000;
                Assert.That(solutions.TryAddReagent(ordinaryInput!.Value, "Water", 1500), Is.True);
                Assert.That(em.System<ItemSlotsSystem>().TryInsert(ordinary, SharedChemMaster.InputSlotName, ordinaryBeaker, visitor), Is.True);
                Transfer(ordinary, visitor, new ReagentId("Water", null));
                Assert.That(Buffer(ordinary).GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(1000)), "Ordinary machines have the same per-type limit.");
                Assert.That(ordinaryContents.Volume, Is.EqualTo(FixedPoint2.New(500)));
                saveLibrary = em.System<PersistentMachineSystem>().SaveNow(library);
                saveChem = em.System<PersistentMachineSystem>().SaveNow(chem);
            });
            await Task.WhenAll(saveLibrary, saveChem);
            var database = pair.Server.ResolveDependency<IServerDbManager>();
            Assert.That((await database.GetMachineStorageAsync(libraryKey))!.Payload, Does.Contain("persistent-blueprint-instance"));
            Assert.That((await database.GetMachineStorageAsync(chemicalKey))!.Payload, Does.Contain("!type:DnaData"));
            await pair.Server.WaitAssertion(() =>
            {
                em.EventBus.RaiseEvent(EventSource.Local, new RoundRestartCleanupEvent());
                em.DeleteEntity(library);
                em.DeleteEntity(chem);
                library = Spawn("BlueprintLithographAcorpPersistent", libraryKey);
                chem = Spawn("ChemMasterPhoenixPersistent", chemicalKey);
            });
            await Loaded(library);
            await Loaded(chem);
            await pair.Server.WaitAssertion(() =>
            {
                em.GetComponent<ApcPowerReceiverComponent>(library).Powered = true;
                var container = em.System<SharedContainerSystem>().GetContainer(library, em.GetComponent<BlueprintReceiverComponent>(library).ContainerId);
                Assert.That(container.ContainedEntities, Has.Count.EqualTo(1));
                var blueprint = container.ContainedEntities.Single();
                var data = em.GetComponent<BlueprintComponent>(blueprint);
                Assert.That(data.ProvidedRecipes.Select(id => id.Id), Is.EquivalentTo(new[] { recipe }));
                Assert.That(data.Modifiers!.InstanceId, Is.EqualTo("persistent-blueprint-instance"));
                Assert.That(data.Modifiers.TimeMultiplier, Is.EqualTo(3));
                Assert.That(data.Modifiers.MaterialMultiplier, Is.EqualTo(2));
                Assert.That(data.Modifiers.ExtraMaterials["Steel"], Is.EqualTo(2));
                var buffer = Buffer(chem);
                Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.New(3000)), "Round teardown must not overwrite the database with an empty buffer.");
                Assert.That(buffer.GetTotalPrototypeQuantity("Blood"), Is.EqualTo(FixedPoint2.New(1000)));
                Assert.That(buffer.Contents.Where(r => r.Reagent.Prototype == "Blood").SelectMany(r => r.Reagent.Data!).OfType<DnaData>().Select(d => d.DNA),
                    Is.EquivalentTo(new[] { "donor-a", "donor-b" }));
                var eject = new BlueprintLibraryEjectMessage(em.GetNetEntity(blueprint)) { Actor = acorp };
                em.EventBus.RaiseLocalEvent(library, eject);
                Assert.That(container.ContainedEntities, Is.Empty);
                saveLibrary = em.System<PersistentMachineSystem>().SaveNow(library);
            });
            await saveLibrary;
            Assert.That((await database.GetMachineStorageAsync(libraryKey))!.Payload, Does.Not.Contain("persistent-blueprint-instance"), "Removing a blueprint must update its database record.");

            EntityUid Spawn(string prototype, string key)
            {
                var uid = em.CreateEntityUninitialized(prototype, map.GridCoords);
                em.GetComponent<PersistentMachineComponent>(uid).StorageId = key;
                em.InitializeAndStartEntity(uid);
                return uid;
            }
            EntityUid Actor(string tag)
            {
                var uid = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                em.AddComponent<AccessComponent>(uid);
                Assert.That(em.System<SharedAccessSystem>().TrySetTags(uid, new ProtoId<AccessLevelPrototype>[] { tag }), Is.True);
                return uid;
            }
            bool OpenDenied(EntityUid uid, EntityUid user)
            {
                var attempt = new ActivatableUIOpenAttemptEvent(user);
                em.EventBus.RaiseLocalEvent(uid, attempt);
                return attempt.Cancelled;
            }
            Solution Buffer(EntityUid uid)
            {
                Assert.That(em.System<SharedSolutionContainerSystem>().TryGetSolution(uid, SharedChemMaster.BufferSolutionName, out _, out var result), Is.True);
                return result!;
            }
            void Transfer(EntityUid uid, EntityUid user, ReagentId reagent)
            {
                var message = new ChemMasterReagentAmountButtonMessage(reagent, ChemMasterReagentAmount.All, false) { Actor = user, UiKey = ChemMasterUiKey.Key };
                em.EventBus.RaiseLocalEvent(uid, message);
            }
            async Task Loaded(EntityUid uid)
            {
                for (var i = 0; i < 250; i++)
                {
                    var ready = false;
                    await pair.Server.WaitAssertion(() =>
                    {
                        var storage = em.GetComponent<PersistentMachineComponent>(uid);
                        Assert.That(storage.Failed, Is.False, "Persistent machine failed to load its database state.");
                        ready = storage.Ready;
                    });
                    if (ready) return;
                    await pair.RunTicksSync(1);
                    await Task.Delay(10);
                }
                Assert.Fail("Persistent machine did not complete database hydration.");
            }
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task ResearchServerPointsAndResearchSurviveTheShipSnapshot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var uid = em.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
                var server = em.GetComponent<ResearchServerComponent>(uid);
                em.System<ResearchSystem>().ModifyServerPoints(uid, 12345 - server.Points);
                var technology = pair.Server.ProtoMan.EnumeratePrototypes<TechnologyPrototype>().First();
                em.System<ResearchSystem>().AddTechnology(uid, technology.ID);
                Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid.Owner, out var yaml, out _), Is.True);
                using var reader = new StringReader(yaml!);
                Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "research server persistence", out var restored, offset: new Vector2(20, 20)), Is.True);
                var loaded = em.EntityQuery<ResearchServerComponent, TransformComponent>()
                    .Single(pair => pair.Item2.GridUid == restored!.Value.Owner).Item1;
                Assert.That(loaded.Points, Is.EqualTo(12345));
                Assert.That(loaded.Id, Is.Not.EqualTo(server.Id), "The runtime server identifier must be rebuilt.");
                var loadedUid = em.EntityQueryEnumerator<ResearchServerComponent, TechnologyDatabaseComponent, TransformComponent>();
                while (loadedUid.MoveNext(out _, out _, out var database, out var xform))
                    if (xform.GridUid == restored!.Value.Owner)
                        Assert.That(database.UnlockedTechnologies.Any(id => id.Id == technology.ID), Is.True);
                em.DeleteEntity(restored!.Value.Owner);
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task ProfessionalCataloguesAreExclusiveAndForbiddenVesselsCannotBeSaved()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var prototypes = pair.Server.ProtoMan;
                var shipyard = em.System<ShipyardSystem>();
                var lists = new Dictionary<string, string[]>
                {
                    ["Mining"] = new[] { "Kestrel", "Bulker", "Barge", "Hauler", "Gornyak" },
                    ["Medical"] = new[] { "CareerMedicalStarter", "Stasis", "Eagle", "Vitalis", "Salomandra", "Caladrius" },
                    ["Engineering"] = new[] { "CareerEngineeringStarter", "Retort", "Gasbender", "Ambition", "Hephaestus", "Comet", "Castor" },
                };
                foreach (var (profession, expected) in lists)
                {
                    var console = em.SpawnEntity("ComputerShipyardCareer" + profession, MapCoordinates.Nullspace);
                    var available = shipyard.GetAvailableShuttles(console, ShipyardConsoleUiKey.Custom);
                    Assert.That(available.available.Concat(available.unavailable), Is.EquivalentTo(expected));
                    foreach (var id in expected)
                    {
                        var vessel = prototypes.Index<VesselPrototype>(id);
                        Assert.That(vessel.CareerProfession, Is.EqualTo(profession), id);
                        Assert.That(vessel.Group, Is.EqualTo(ShipyardConsoleUiKey.Custom), id);
                        Assert.That(vessel.CanSave, Is.False, id);
                        Assert.That(vessel.Name, Does.StartWith(profession + " "), id);
                        Assert.That(prototypes.Index<GameMapPrototype>(id).Stations.Values.Single().StationPrototype.Id, Is.EqualTo("Career" + profession + "Station"));
                    }
                    em.DeleteEntity(console);
                }
                var ordinary = em.SpawnEntity("ComputerShipyard", MapCoordinates.Nullspace);
                var standard = shipyard.GetAvailableShuttles(ordinary, ShipyardConsoleUiKey.Shipyard);
                Assert.That(standard.available.Concat(standard.unavailable), Does.Contain("Prospector"));
                Assert.That(standard.available.Concat(standard.unavailable).Intersect(lists.Values.SelectMany(list => list)), Is.Empty);
                foreach (var vessel in prototypes.EnumeratePrototypes<VesselPrototype>().Where(vessel => vessel.CareerProfession != null ||
                             vessel.Group is ShipyardConsoleUiKey.Acorp or ShipyardConsoleUiKey.Navy or ShipyardConsoleUiKey.Security or ShipyardConsoleUiKey.Sr or ShipyardConsoleUiKey.Separatist))
                    Assert.That(vessel.CanSave, Is.False, vessel.ID);
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}

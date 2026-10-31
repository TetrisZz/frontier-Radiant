using System;
using System.IO;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Server._Triad.Shipyard;
using Content.Server._NF.Shipyard.Systems;
using Content.Server.Shuttles.Systems;
using Content.Shared._HL.Shipyard;
using Content.Shared._radiant.Shipyard;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Shuttles.Components;
using Content.Server.Gravity;
using Content.Server._NF.BindToStation;
using Content.Server.Materials;
using Content.Server._radiant.Storage;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.Steps;
using Content.Shared.Store.Components;
using Content.Shared._Triad.Shipyard.Save;
using Robust.Shared.Prototypes;
using Content.Server.Power.Components;
using Content.Shared._NF.BindToStation;
using Content.Shared.Gravity;
using Content.Shared.Materials;
using Content.Shared.SprayPainter.Prototypes;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.Shared.Serialization.Manager.Attributes;
using Robust.Shared.Log;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

[TestFixture]
public sealed class ShipSaveRoundTripTest
{
    [Test]
    public async Task LathesAndSilosLoadEmptyAndShipOwnershipSurvivesSignedSnapshot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var entities = pair.Server.EntMan;
            var machines = new List<EntityUid>();
            var owner = new NetUserId(Guid.NewGuid());
            const string character = "Alice Example";
            await pair.Server.WaitAssertion(() =>
            {
                var materials = entities.System<MaterialStorageSystem>();
                foreach (var prototype in new[] { "ServiceTechFab", "EngineeringTechFab", "Autolathe", "MachineMaterialSilo" })
                {
                    var uid = entities.SpawnEntity(prototype, map.GridCoords);
                    Assert.That(materials.TryChangeMaterialAmount(uid, "Steel", 1234), Is.True);
                    machines.Add(uid);
                }
            });
            await pair.Server.WaitRunTicks(2);
            await pair.Server.WaitAssertion(() =>
            {
                var saves = entities.System<ShipyardGridSaveSystem>();
                Assert.That(saves.TryBuildShipSaveYaml(map.Grid.Owner, out var yaml, out _, owner, character), Is.True);
                foreach (var uid in machines)
                    Assert.That(entities.GetComponent<MaterialStorageComponent>(uid).Storage["Steel"], Is.EqualTo(1234),
                        "Saving must not empty the live machine before the save is committed.");
                var signed = entities.System<ShipSaveSigningSystem>().Sign(yaml!, 0);
                var file = AuthenticatedShipFile.FromShipFile(signed.ShipFileString());
                Assert.That(file.IsShipSigned(), Is.True);
                using var reader = new StringReader(file.ShipYamlString());
                Assert.That(entities.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "bound ship", out var loaded,
                    offset: new Vector2(20, 20)), Is.True);
                var saved = entities.GetComponent<SavedShipComponent>(loaded.Value.Owner);
                Assert.That(saved.IsOwner(owner, character), Is.True);
                Assert.That(saved.IsOwner(owner, "Bob Example"), Is.False, "Another character on the same account must not load the ship.");
                Assert.That(saved.IsOwner(new NetUserId(Guid.NewGuid()), character), Is.False,
                    "Matching names on another account must not grant ownership.");
                Assert.That(new SavedShipComponent().HasOwner, Is.False, "Legacy saves require administrative migration.");
                var query = entities.EntityQueryEnumerator<MaterialStorageComponent, TransformComponent>();
                var loadedMachines = new List<EntityUid>();
                while (query.MoveNext(out var uid, out var materials, out var xform))
                {
                    if (xform.GridUid != loaded.Value.Owner)
                        continue;
                    loadedMachines.Add(uid);
                    Assert.That(materials.Storage.Values.Sum(), Is.Zero, "Saved machines must load without materials.");
                    Assert.That(entities.System<MaterialStorageSystem>().TryChangeMaterialAmount(uid, "Steel", 456), Is.True);
                }
                Assert.That(loadedMachines, Has.Count.EqualTo(machines.Count));
                // Old files may still contain materials: the load-side cleanup must cover them too.
                saves.ClearLoadedMachineMaterials(loaded.Value.Owner);
                foreach (var uid in loadedMachines)
                    Assert.That(entities.GetComponent<MaterialStorageComponent>(uid).Storage.Values.Sum(), Is.Zero);
                foreach (var uid in machines)
                    Assert.That(entities.GetComponent<MaterialStorageComponent>(uid).Storage["Steel"], Is.EqualTo(1234),
                        "Load-side cleanup must not affect machines on another grid.");
                entities.DeleteEntity(loaded.Value.Owner);
                TestContext.Out.WriteLine("Lathe materials and signed character ownership assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Lathe/ownership regression failure: {exception}");
            throw;
        }
        // These server-only serialization tests use isolated worlds. Check server
        // errors explicitly rather than unrelated errors in the unused client's assets.
        Assert.That(pair.ServerLogHandler.FailingLogs, Is.Empty);
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task WallSafePreservesCodeAndContentsAndRejectsSecondSafe()
    {
        await using var pair = await PoolManager.GetServerClient();
        var server = pair.Server;
        var map = await pair.CreateTestMap();
        var entities = server.ResolveDependency<IEntityManager>();
        EntityUid safe = default;
        EntityUid secondSafe = default;
        EntityUid paper = default;
        await server.WaitAssertion(() =>
        {
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var recipe = prototypes.Index<ConstructionPrototype>("PasswordWallSafe");
            Assert.That(recipe.SetName, Is.Not.Null);
            Assert.That(recipe.SetDescription, Is.Not.Null);
            Assert.That(prototypes.Index<ShipSaveLimitsPrototype>("ShipSaveLimits")
                .Limits["PasswordWallSafe"], Is.EqualTo(1));
            var board = entities.SpawnEntity("PasswordWallSafeCircuitboard", map.GridCoords);
            var boardStep = prototypes.Index<ConstructionGraphPrototype>("PasswordWallSafe")
                .Edge("frame", "safe")!.Steps.OfType<TagConstructionGraphStep>().Single();
            Assert.That(boardStep.EntityValid(board, entities, server.ResolveDependency<IComponentFactory>()), Is.True);
            safe = entities.SpawnEntity("PasswordWallSafe", map.GridCoords);
            entities.GetComponent<PasswordSafeCodeComponent>(safe).Code = "012345";
            Assert.That(entities.GetComponent<HLPersistOnShipSaveComponent>(safe).SaveContents, Is.True);
            paper = entities.SpawnEntity("Paper", map.GridCoords);
            Assert.That(entities.System<SharedStorageSystem>().Insert(safe, paper, out _, playSound: false), Is.True);
            var pda = entities.SpawnEntity("PassengerPDA", map.GridCoords);
            Assert.That(entities.System<SharedStorageSystem>().Insert(safe, pda, out _, playSound: false), Is.True);
            secondSafe = entities.SpawnEntity("PasswordWallSafe", map.GridCoords);
        });
        await server.WaitRunTicks(2);
        await server.WaitAssertion(() =>
        {
            var saves = entities.System<ShipyardGridSaveSystem>();
            Assert.That(saves.CheckGridEntityLimits(map.Grid.Owner, out var message), Is.False);
            Assert.That(message, Is.Not.Empty);
            Assert.That(entities.EntityExists(paper), Is.True);
            entities.DeleteEntity(secondSafe);
            Assert.That(saves.CheckGridEntityLimits(map.Grid.Owner, out _), Is.True);
            var livePda = entities.GetComponent<StorageComponent>(safe).StoredItems.Keys.Single(item =>
                entities.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == "PassengerPDA");
            var liveStore = entities.GetComponent<StoreComponent>(livePda);
            var startingMap = liveStore.StartingMap;
            Assert.That(saves.TryBuildShipSaveYaml(map.Grid.Owner, out var yaml, out _), Is.True);
            Assert.That(liveStore.StartingMap, Is.EqualTo(startingMap), "Export must not mutate the live store.");
            Assert.That(entities.GetComponent<StorageComponent>(safe).StoredItems.ContainsKey(paper), Is.True);
            using var reader = new StringReader(yaml);
            Assert.That(entities.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader,
                "saved safe", out var loaded, offset: new Vector2(20, 20)), Is.True);
            var query = entities.EntityQueryEnumerator<PasswordSafeCodeComponent, TransformComponent>();
            var count = 0;
            while (query.MoveNext(out var uid, out var code, out var xform))
            {
                if (xform.GridUid != loaded.Value.Owner)
                    continue;
                count++;
                Assert.That(code.Code, Is.EqualTo("012345"));
                var items = entities.GetComponent<StorageComponent>(uid).StoredItems.Keys.ToArray();
                Assert.That(items.Select(item => entities.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID),
                    Is.EquivalentTo(new[] { "Paper", "PassengerPDA" }));
                var pda = items.Single(item => entities.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID == "PassengerPDA");
                Assert.That(entities.System<SharedContainerSystem>().GetContainer(pda, "PDA-id").ContainedEntities,
                    Has.Count.EqualTo(1), "ID cards nested in safe contents must also survive sanitation.");
            }
            Assert.That(count, Is.EqualTo(1));
            entities.DeleteEntity(loaded.Value.Owner);
            TestContext.Out.WriteLine("Wall safe persistence and limit assertions completed");
        });
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task FurnitureAndBlanketsSurviveWithOnlyIntendedContents()
    {
        await using var pair = await PoolManager.GetServerClient();
        try
        {
            var server = pair.Server;
            var entMan = server.ResolveDependency<IEntityManager>();
            var map = await pair.CreateTestMap();
            var storageSystem = entMan.System<SharedStorageSystem>();
            var containers = entMan.System<SharedContainerSystem>();
            var emptyFurniture = new[]
            {
                "DresserFilled", "filingCabinetRandom", "filingCabinetTallRandom", "filingCabinetDrawerRandom",
                "FilingCabinetRadiant", "ChestDrawerRadiant", "TallCabinetRadiant", "EmploymentCabinet",
                "LockerWallEVAColorAtmosTechFilled",
            };
            var emptyContainers = new[]
            {
                "ClosetTool", "ClosetWall", "LockerElectricalSuppliesFilled",
                "CrateGenericSteel", "CrateServiceJanitorialSupplies",
            };
            var plants = new[] { "PottedPlant0", "PottedPlant26", "PottedPlant30" };
            var sourceContainerContents = new Dictionary<EntityUid, EntityUid[]>();
            var sourceContents = new Dictionary<EntityUid, EntityUid[]>();
            var expectedBooks = new Dictionary<string, string[]>();
            var seats = new[]
            {
                "Chair", "ChairWood", "ChairFolding", "ComfyChair", "ArmChair",
                "ChairOfficeLight", "ChairOfficeDark", "ChairOfficeGrey",
            };
            await server.WaitAssertion(() =>
            {
                foreach (var prototype in plants)
                {
                    var plant = entMan.SpawnEntity(prototype, map.GridCoords);
                    Assert.That(entMan.HasComponent<SecretStashComponent>(plant), Is.False);
                    Assert.That(containers.TryGetContainer(plant, "stash", out _), Is.False);
                }
                foreach (var prototype in emptyContainers)
                {
                    var furniture = entMan.SpawnEntity(prototype, map.GridCoords);
                    entMan.System<SharedTransformSystem>().Unanchor(furniture);
                    var container = containers.GetContainer(furniture, "entity_storage");
                    var item = entMan.SpawnEntity("BedsheetBlue", map.GridCoords);
                    Assert.That(containers.Insert(item, container), Is.True);
                    sourceContainerContents.Add(furniture, container.ContainedEntities.ToArray());
                }
                foreach (var prototype in seats)
                {
                    var seat = entMan.SpawnEntity(prototype, map.GridCoords);
                    entMan.System<SharedTransformSystem>().Unanchor(seat);
                    Assert.That(entMan.GetComponent<TransformComponent>(seat).Anchored, Is.False);
                }
                entMan.SpawnEntity("FireAxeCabinetFilled", map.GridCoords);
                foreach (var prototype in new[] { "Bookshelf", "BookshelfFilled" })
                {
                    var shelf = entMan.SpawnEntity(prototype, map.GridCoords);
                    if (prototype == "Bookshelf")
                    {
                        var book = entMan.SpawnEntity("BookSpaceEncyclopedia", map.GridCoords);
                        Assert.That(storageSystem.Insert(shelf, book, out _, playSound: false), Is.True);
                    }
                    var items = entMan.GetComponent<StorageComponent>(shelf).StoredItems.Keys.ToArray();
                    sourceContents.Add(shelf, items);
                    expectedBooks.Add(prototype, items.Select(item =>
                        entMan.GetComponent<MetaDataComponent>(item).EntityPrototype!.ID).ToArray());
                }
                foreach (var prototype in emptyFurniture)
                {
                    var furniture = entMan.SpawnEntity(prototype, map.GridCoords);
                    var item = entMan.SpawnEntity("BedsheetBlue", map.GridCoords);
                    Assert.That(storageSystem.Insert(furniture, item, out _, playSound: false), Is.True);
                    sourceContents.Add(furniture,
                        entMan.GetComponent<StorageComponent>(furniture).StoredItems.Keys.ToArray());
                }
                entMan.SpawnEntity("BedsheetWhite", map.GridCoords);
                entMan.SpawnEntity("DoubleBedsheetWhite", map.GridCoords);
            });
            await server.WaitAssertion(() =>
            {
                Assert.That(entMan.System<ShipyardGridSaveSystem>()
                    .TryBuildShipSaveYaml(map.Grid.Owner, out var yaml, out _), Is.True);
                foreach (var (source, contents) in sourceContents)
                {
                    Assert.That(entMan.GetComponent<StorageComponent>(source).StoredItems.Keys,
                        Is.EquivalentTo(contents), "Snapshot must not clear live furniture.");
                    Assert.That(contents.All(entMan.EntityExists), Is.True);
                }
                foreach (var (source, contents) in sourceContainerContents)
                {
                    Assert.That(containers.GetContainer(source, "entity_storage").ContainedEntities,
                        Is.EquivalentTo(contents), "Snapshot must not empty live lockers or crates.");
                    Assert.That(contents.All(entMan.EntityExists), Is.True);
                }
                using var reader = new StringReader(yaml);
                Assert.That(entMan.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader,
                    "saved furniture", out var loaded, offset: new Vector2(20, 20)), Is.True);
                var seen = new List<string>();
                var query = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                while (query.MoveNext(out var uid, out var meta, out var xform))
                {
                    if (xform.GridUid != loaded.Value.Owner || meta.EntityPrototype is not { } proto)
                        continue;
                    seen.Add(proto.ID);
                    if (emptyContainers.Contains(proto.ID))
                        Assert.That(containers.GetContainer(uid, "entity_storage").ContainedEntities,
                            Is.Empty, $"{proto.ID} must load empty without respawning its fill.");
                    if (seats.Contains(proto.ID))
                        Assert.That(xform.Anchored, Is.False, "Saving must not anchor loose seats.");
                    if (emptyFurniture.Contains(proto.ID))
                        Assert.That(entMan.GetComponent<StorageComponent>(uid).StoredItems, Is.Empty,
                            $"{proto.ID} must load empty, without prototype fill respawning.");
                    if (expectedBooks.TryGetValue(proto.ID, out var books))
                    {
                        var loadedBooks = entMan.GetComponent<StorageComponent>(uid).StoredItems.Keys
                            .Select(item => entMan.GetComponent<MetaDataComponent>(item).EntityPrototype!.ID);
                        Assert.That(loadedBooks, Is.EquivalentTo(books));
                    }
                    if (proto.ID == "FireAxeCabinetFilled")
                    {
                        var axe = containers.GetContainer(uid, "ItemCabinet").ContainedEntities.Single();
                        Assert.That(entMan.GetComponent<MetaDataComponent>(axe).EntityPrototype?.ID, Is.EqualTo("FireAxe"));
                    }
                }
                foreach (var prototype in emptyFurniture.Concat(emptyContainers).Concat(plants).Concat(seats).Concat(new[]
                    { "Bookshelf", "BookshelfFilled", "FireAxeCabinetFilled", "BedsheetWhite", "DoubleBedsheetWhite" }))
                    Assert.That(seen.Count(id => id == prototype), Is.EqualTo(1), prototype);
                Assert.That(seen, Does.Not.Contain("BedsheetBlue"));
                entMan.DeleteEntity(loaded.Value.Owner);
                TestContext.Out.WriteLine("Plants, empty lockers and crates round-trip assertions completed");
            });
            await pair.CleanReturnAsync();
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Furniture save regression failure: {exception}");
            throw;
        }
    }

    [Test]
    public async Task PersistentStorageContentsSurviveSaveAndLoad()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var server = pair.Server;
            var entMan = server.ResolveDependency<IEntityManager>();
            var map = await pair.CreateTestMap();
            var saveSystem = entMan.System<ShipyardGridSaveSystem>();
            var mapLoader = entMan.System<MapLoaderSystem>();
            var containers = entMan.System<SharedContainerSystem>();
            var storageSystem = entMan.System<SharedStorageSystem>();
            EntityUid loose = default;
            EntityUid cabinet = default;
            EntityUid storage = default;
            EntityUid storedItem = default;
            ItemStorageLocation storedLocation = default;
            EntityUid[] cabinetContents = [];
            EntityUid airlock = default;
            EntityUid electronics = default;
            EntityUid silo = default;
            EntityUid generator = default;
            EntityUid researchServer = default;
            await server.WaitAssertion(() =>
            {
                var shipyard = entMan.System<ShipyardSystem>();
                var saved = entMan.EnsureComponent<SavedShipComponent>(map.Grid.Owner);
                saved.VesselId = "Firefighter";
                shipyard.RestoreSavedShipIFF(map.Grid.Owner);
                var iff = entMan.GetComponent<IFFComponent>(map.Grid.Owner);
                Assert.That(iff.Color, Is.EqualTo(Color.White));
                Assert.That(iff.Flags, Is.EqualTo(IFFFlags.IsPlayerShuttle));
                entMan.RemoveComponent<IFFComponent>(map.Grid.Owner);
                saved.VesselId = null;
                shipyard.RestoreSavedShipIFF(map.Grid.Owner);
                iff = entMan.GetComponent<IFFComponent>(map.Grid.Owner);
                Assert.That(iff.Color, Is.EqualTo(Color.White));
                Assert.That(iff.Flags, Is.EqualTo(IFFFlags.IsPlayerShuttle));
                var shuttles = entMan.System<ShuttleSystem>();
                shuttles.SetIFFColor(map.Grid.Owner, Color.Red);
                shuttles.AddIFFFlag(map.Grid.Owner, IFFFlags.HideLabel);
                shuttles.SetServiceFlags(map.Grid.Owner, ServiceFlags.Trade);
                shuttles.SetIFFReadOnly(map.Grid.Owner, true);
                shipyard.RestoreSavedShipIFF(map.Grid.Owner);
                Assert.That(iff.Color, Is.EqualTo(Color.Red), "Existing IFF must not be replaced with defaults.");

                storage = entMan.SpawnEntity("CivilianStorageStash", map.GridCoords);
                Assert.That(entMan.GetComponent<HLPersistOnShipSaveComponent>(storage).SaveContents, Is.True);
                storedItem = entMan.SpawnEntity("Screwdriver", map.GridCoords);
                Assert.That(storageSystem.Insert(storage, storedItem, out _, playSound: false), Is.True);
                storedLocation = entMan.GetComponent<StorageComponent>(storage).StoredItems[storedItem];
                cabinet = entMan.SpawnEntity("LockerWallEVAColorAtmosTechFilled", map.GridCoords);
                cabinetContents = entMan.GetComponent<StorageComponent>(cabinet).StoredItems.Keys.ToArray();
                Assert.That(cabinetContents.Length, Is.GreaterThanOrEqualTo(2));
                loose = entMan.SpawnEntity("Screwdriver", map.GridCoords);
                airlock = entMan.SpawnEntity("AirlockEngineeringLocked", map.GridCoords);
                electronics = containers.GetContainer(airlock, "board").ContainedEntities.Single();
                var reader = entMan.GetComponent<AccessReaderComponent>(electronics);
                entMan.System<AccessReaderSystem>().ClearAccesses((electronics, reader));
                Assert.That(reader.AccessLists, Is.Empty);
                entMan.System<SharedAppearanceSystem>().SetData(airlock, PaintableVisuals.Prototype, "AirlockMedical");
                entMan.SpawnEntity("DefibrillatorCabinetFilled", map.GridCoords);
                entMan.SpawnEntity("ExtinguisherCabinetFilled", map.GridCoords);
                silo = entMan.SpawnEntity("MachineMaterialSilo", map.GridCoords);
                Assert.That(entMan.System<MaterialStorageSystem>().TryChangeMaterialAmount(silo, "Steel", 1234), Is.True);
                researchServer = entMan.SpawnEntity("ResearchAndDevelopmentServer", map.GridCoords);
                entMan.System<BindToStationSystem>().BindToStation(silo, map.Grid.Owner);
                entMan.System<BindToStationSystem>().BindToStation(researchServer, map.Grid.Owner);
                entMan.EnsureComponent<GravityComponent>(map.Grid.Owner);
                generator = entMan.SpawnEntity("GravityGenerator", map.GridCoords);
                // Seed the already-active state that a running generator exports.
#pragma warning disable RA0002
                entMan.GetComponent<PowerChargeComponent>(generator).Active = true;
#pragma warning restore RA0002
                var activated = new Content.Server.Power.EntitySystems.ChargedMachineActivatedEvent();
                entMan.EventBus.RaiseLocalEvent(generator, ref activated);
            });

            await server.WaitAssertion(() =>
            {
                Assert.That(saveSystem.TryBuildShipSaveYaml(map.Grid.Owner, out var yaml, out _), Is.True);
                Assert.That(entMan.EntityExists(loose), Is.True, "Snapshot must not delete loose items.");
                Assert.That(entMan.GetComponent<StorageComponent>(cabinet).StoredItems.Keys,
                    Is.EquivalentTo(cabinetContents), "Snapshot must not remove excluded cabinet contents.");
                Assert.That(cabinetContents.All(entMan.EntityExists), Is.True);
                Assert.That(entMan.GetComponent<StorageComponent>(storage).StoredItems[storedItem],
                    Is.EqualTo(storedLocation));
                Assert.That(containers.GetContainer(airlock, "board").ContainedEntities,
                    Does.Contain(electronics), "Snapshot must not remove installed door electronics.");
                Assert.That(entMan.GetComponent<AccessReaderComponent>(electronics).AccessLists, Is.Empty);
                Assert.That(entMan.GetComponent<StationBoundObjectComponent>(silo).BoundStation, Is.EqualTo(map.Grid.Owner));
                Assert.That(entMan.GetComponent<MaterialStorageComponent>(silo).Storage["Steel"], Is.EqualTo(1234));
                Assert.That(entMan.GetComponent<StationBoundObjectComponent>(researchServer).Enabled, Is.True);
                Assert.That(entMan.GetComponent<GravityGeneratorComponent>(generator).GravityActive, Is.True);
                using var reader = new StringReader(yaml);
                Assert.That(mapLoader.TryLoadGrid(map.MapId, reader, "ship save round trip", out var loaded,
                    offset: new Vector2(20, 20)), Is.True);
                var loadedIff = entMan.GetComponent<IFFComponent>(loaded.Value.Owner);
                Assert.That(loadedIff.Color, Is.EqualTo(Color.Red));
                Assert.That(loadedIff.Flags, Is.EqualTo(IFFFlags.IsPlayerShuttle | IFFFlags.HideLabel));
                Assert.That(loadedIff.ServiceFlags, Is.EqualTo(ServiceFlags.Trade));
                Assert.That(loadedIff.ReadOnly, Is.True);
                Assert.That(entMan.GetComponent<GravityComponent>(loaded.Value.Owner).Enabled, Is.True);

                var query = entMan.EntityQueryEnumerator<HLPersistOnShipSaveComponent, TransformComponent>();
                EntityUid loadedStorage = default;
                while (query.MoveNext(out var uid, out _, out var xform))
                {
                    if (xform.GridUid == loaded.Value.Owner
                        && entMan.GetComponent<MetaDataComponent>(uid).EntityPrototype?.ID == "CivilianStorageStash")
                        loadedStorage = uid;
                }
                Assert.That(loadedStorage, Is.Not.EqualTo(default(EntityUid)));
                var loadedContents = entMan.GetComponent<StorageComponent>(loadedStorage).StoredItems;
                Assert.That(loadedContents, Has.Count.EqualTo(1));
                Assert.That(loadedContents.Values.Single(), Is.EqualTo(storedLocation));
                Assert.That(entMan.GetComponent<MetaDataComponent>(loadedContents.Keys.Single())
                    .EntityPrototype?.ID, Is.EqualTo("Screwdriver"));

                var loadedItems = entMan.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
                var screwdriverCount = 0;
                EntityUid loadedAirlock = default;
                EntityUid loadedCabinet = default;
                var restoredFixtures = new HashSet<string>();
                while (loadedItems.MoveNext(out var uid, out var meta, out var xform))
                {
                    if (xform.GridUid != loaded.Value.Owner)
                        continue;
                    if (meta.EntityPrototype?.ID == "Screwdriver")
                        screwdriverCount++;
                    if (meta.EntityPrototype?.ID == "AirlockEngineeringLocked")
                        loadedAirlock = uid;
                    if (meta.EntityPrototype?.ID == "LockerWallEVAColorAtmosTechFilled")
                        loadedCabinet = uid;
                    if (meta.EntityPrototype?.ID == "MachineMaterialSilo")
                    {
                        restoredFixtures.Add(meta.EntityPrototype.ID);
                        Assert.That(entMan.GetComponent<MaterialStorageComponent>(uid).Storage.Values.Sum(), Is.Zero,
                            "The material silo must survive as empty equipment.");
                        var machine = entMan.GetComponent<Content.Server.Construction.Components.MachineComponent>(uid);
                        var board = machine.BoardContainer.ContainedEntities.Single();
                        Assert.That(entMan.GetComponent<MetaDataComponent>(board).EntityPrototype?.ID,
                            Is.EqualTo("MaterialSiloMachineCircuitboard"), "Installed components must remain usable.");
                    }
                    if (meta.EntityPrototype?.ID is "ResearchAndDevelopmentServer")
                    {
                        restoredFixtures.Add(meta.EntityPrototype.ID);
                        var binding = entMan.GetComponent<StationBoundObjectComponent>(uid);
                        Assert.That(binding.BoundStation, Is.Null);
                        Assert.That(binding.Enabled, Is.False);
                    }
                    if (meta.EntityPrototype?.ID is "DefibrillatorCabinetFilled" or "ExtinguisherCabinetFilled")
                    {
                        restoredFixtures.Add(meta.EntityPrototype.ID);
                        var item = containers.GetContainer(uid, "ItemCabinet").ContainedEntities.Single();
                        Assert.That(entMan.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID,
                            Is.EqualTo(meta.EntityPrototype.ID == "DefibrillatorCabinetFilled" ? "Defibrillator" : "FireExtinguisher"));
                    }
                    if (meta.EntityPrototype?.ID == "GravityGenerator")
                    {
                        restoredFixtures.Add(meta.EntityPrototype.ID);
                        Assert.That(entMan.GetComponent<GravityGeneratorComponent>(uid).GravityActive, Is.True);
                    }
                }
                Assert.That(restoredFixtures, Is.EquivalentTo(new[]
                {
                    "ResearchAndDevelopmentServer", "GravityGenerator", "MachineMaterialSilo",
                    "DefibrillatorCabinetFilled", "ExtinguisherCabinetFilled"
                }));
                Assert.That(screwdriverCount, Is.EqualTo(1), "Loose items must only be excluded from the snapshot.");
                Assert.That(loadedAirlock, Is.Not.EqualTo(default(EntityUid)));
                Assert.That(loadedCabinet, Is.Not.EqualTo(default(EntityUid)));
                Assert.That(entMan.GetComponent<StorageComponent>(loadedCabinet).StoredItems, Is.Empty);
                Assert.That(entMan.System<SharedAppearanceSystem>().TryGetData<string>(loadedAirlock,
                    PaintableVisuals.Prototype, out var paint), Is.True);
                Assert.That(paint, Is.EqualTo("AirlockMedical"));
                var loadedBoard = containers.GetContainer(loadedAirlock, "board").ContainedEntities.Single();
                Assert.That(entMan.GetComponent<AccessReaderComponent>(loadedBoard).AccessLists, Is.Empty,
                    "Saved electronics must retain their configured access, not restore prototype restrictions.");
                entMan.DeleteEntity(loaded.Value.Owner);
            });

            await server.WaitAssertion(() =>
            {
                entMan.AddComponent<FailingShipSaveComponent>(map.Grid.Owner);
                var failureLevel = pair.ServerLogHandler.FailureLevel;
                // This synchronous assertion intentionally makes the serializer log an error.
                pair.ServerLogHandler.FailureLevel = LogLevel.Fatal;
                try
                {
                    Assert.That(() => saveSystem.TryBuildShipSaveYaml(map.Grid.Owner, out _, out _), Throws.Exception);
                }
                finally
                {
                    pair.ServerLogHandler.FailureLevel = failureLevel;
                }
                Assert.That(entMan.EntityExists(loose), Is.True, "Failed serialization must not delete items.");
                Assert.That(entMan.GetComponent<StorageComponent>(cabinet).StoredItems.Keys,
                    Is.EquivalentTo(cabinetContents));
                Assert.That(containers.GetContainer(airlock, "board").ContainedEntities, Does.Contain(electronics));
                Assert.That(entMan.GetComponent<AccessReaderComponent>(electronics).AccessLists, Is.Empty);
                entMan.RemoveComponent<FailingShipSaveComponent>(map.Grid.Owner);
            });

            Assert.That(pair.ServerLogHandler.FailingLogs, Is.Empty);
            await pair.CleanReturnAsync();
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Ship save regression failure: {exception}");
            throw;
        }
    }

}

[RegisterComponent]
public sealed partial class FailingShipSaveComponent : Component
{
    [DataField] public UnserializableShipSaveState State = new();
}

public sealed class UnserializableShipSaveState;

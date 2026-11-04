using System;
using Content.Server._radiant.Dossiers;
using Content.Server.Station.Systems;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Shared._radiant.Dossiers;
using Content.Shared.Access.Components;
using Content.Shared.Forensics.Components;
using Content.Shared.Power.Components;
using Content.Shared.Station.Components;
using Content.Shared.StationRecords;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Access;

public sealed class PersonnelDossierScopeTest
{
    [Test]
    public async Task PersonnelDossiersFollowStaffRegistryInsteadOfLocationOrSector()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var first = await pair.CreateTestMap();
            var second = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.EntMan;
                var stations = entities.System<StationSystem>();
                var records = entities.System<StationRecordsSystem>();
                var dossiers = entities.System<DossierSystem>();
                EntityUid MakeStation(EntityUid grid)
                {
                    var station = entities.SpawnEntity(null, MapCoordinates.Nullspace);
                    entities.EnsureComponent<StationDataComponent>(station);
                    entities.EnsureComponent<StationRecordsComponent>(station);
                    stations.AddGridToStation(station, grid);
                    return station;
                }
                var firstStation = MakeStation(first.Grid);
                var secondStation = MakeStation(second.Grid);
                var console = entities.SpawnEntity("ComputerTabletopStationRecords", first.GridCoords);
                entities.RemoveComponent<AccessReaderComponent>(console);
                entities.RemoveComponent<ActivatableUIRequiresPowerComponent>(console);
                var user = entities.SpawnEntity("MobHuman", first.GridCoords);
                var member = entities.SpawnEntity("MobHuman", second.GridCoords);
                var visitor = entities.SpawnEntity("MobHuman", first.GridCoords);
                entities.System<MetaDataSystem>().SetEntityName(member, "Same Name");
                entities.System<MetaDataSystem>().SetEntityName(visitor, "Same Name");
                var memberHolder = entities.EnsureComponent<DossierHolderComponent>(member);
                memberHolder.Loaded = true;
                entities.EnsureComponent<DossierHolderComponent>(visitor).Loaded = true;
                GeneralStationRecord RecordFor(EntityUid person) => new()
                {
                    Name = "Same Name",
                    DNA = entities.GetComponent<DnaComponent>(person).DNA,
                    Fingerprint = entities.GetComponent<FingerprintComponent>(person).Fingerprint,
                };
                var memberRecord = records.AddRecordEntry(firstStation, RecordFor(member));
                records.AddRecordEntry(secondStation, RecordFor(visitor));
                Assert.That(dossiers.TryOpenSelected(console, user, member), Is.True,
                    "An employed person remains available while away from their station.");
                DossierUiState State() => (DossierUiState) entities.GetComponent<UserInterfaceComponent>(console).States[DossierUiKey.Key];
                Assert.That(State().People.ContainsKey(entities.GetNetEntity(member)), Is.True);
                Assert.That(State().People.ContainsKey(entities.GetNetEntity(visitor)), Is.False,
                    "A visitor with the same name cannot use another person's personnel record.");
                Assert.That(dossiers.TryOpenSelected(console, user, visitor), Is.False,
                    "Supplying a foreign entity directly must be rejected too.");

                var archive = entities.SpawnEntity(null, MapCoordinates.Nullspace);
                var archived = entities.EnsureComponent<DossierHolderComponent>(archive);
                archived.Loaded = true;
                archived.Archived = true;
                archived.DisplayName = "Same Name";
                archived.PersonnelDna = entities.GetComponent<DnaComponent>(member).DNA;
                archived.PersonnelFingerprint = entities.GetComponent<FingerprintComponent>(member).Fingerprint;
                memberHolder.HiddenInCryo = true;
                Assert.That(dossiers.TryOpenSelected(console, user, archive), Is.True,
                    "Cryo records must retain their personnel affiliation without a body on station.");
                records.RemoveRecord(memberRecord);
                Assert.That(State().Selected, Is.Null, "An open dossier must clear when its staff record is deleted.");
                Assert.That(dossiers.TryOpenSelected(console, user, archive), Is.False,
                    "Removing the personnel record must revoke dossier access.");

                var orphanConsole = entities.SpawnEntity("ComputerTabletopStationRecords", MapCoordinates.Nullspace);
                entities.RemoveComponent<AccessReaderComponent>(orphanConsole);
                Assert.That(dossiers.TryOpenSelected(orphanConsole, user, visitor), Is.False,
                    "A console without a station must not fall back to the whole sector.");
                entities.GetComponent<DossierConsoleComponent>(console).Kind = DossierKind.Medical;
                Assert.That(dossiers.TryOpenSelected(console, user, visitor), Is.True,
                    "Medical terminals retain their existing medical-record scope.");
            });
            await pair.ReallyBeIdle();
            await pair.CleanReturnAsync();
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Personnel dossier scope regression failure: {exception}");
            throw;
        }
    }
}

using System.Linq;
using Content.Server.StationRecords.Components;
using Content.Shared._radiant.Dossiers;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Power.Components;
using Content.Shared.Roles;
using Content.Shared.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Access;

public sealed class SeparatistsCommanderAccessTest
{
    [Test]
    public async Task CommanderDevicesOnlyAcceptCommanderAccessAndKeepOrdinaryDevicesUnchanged()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var prototypes = pair.Server.ProtoMan;
                var access = em.System<SharedAccessSystem>();
                var readers = em.System<AccessReaderSystem>();
                ProtoId<AccessLevelPrototype> commanderAccess = "SeparatistsCommander";
                var loc = pair.Server.ResolveDependency<ILocalizationManager>();
                Assert.That(loc.GetString(prototypes.Index(commanderAccess).Name), Is.EqualTo("Командир взвода"));

                var doors = new[]
                {
                    em.SpawnEntity("AirlockSeparatistsMiningCommanderLocked", map.GridCoords),
                    em.SpawnEntity("AirlockSeparatistsMiningGlassCommanderLocked", map.GridCoords),
                };
                foreach (var door in doors)
                {
                    Assert.That(readers.GetMainAccessReader(door, out var board), Is.True);
                    Assert.That(em.GetComponent<MetaDataComponent>(board!.Value.Owner).EntityPrototype!.ID,
                        Is.EqualTo("DoorElectronicsSeparatistsCommander"));
                    Assert.That(board.Value.Comp.AccessLists, Has.Count.EqualTo(1));
                    Assert.That(board.Value.Comp.AccessLists.Single(), Is.EquivalentTo(new[] { commanderAccess }));
                }

                var ordinaryDoors = new[]
                {
                    em.SpawnEntity("AirlockSeparatistsMiningLocked", map.GridCoords),
                    em.SpawnEntity("AirlockSeparatistsMiningGlassLocked", map.GridCoords),
                };
                var console = em.SpawnEntity("ComputerTabletopStationRecordsSeparatistsCommander", map.GridCoords);
                Assert.That(em.HasComponent<ActivatableUIRequiresAccessComponent>(console), Is.True);
                Assert.That(em.HasComponent<GeneralStationRecordConsoleComponent>(console), Is.True);
                Assert.That(em.GetComponent<DossierConsoleComponent>(console).Kind, Is.EqualTo(DossierKind.Personal));
                var localized = loc.GetEntityData("ComputerTabletopStationRecordsSeparatistsCommander");
                var originalLocalized = loc.GetEntityData("ComputerTabletopStationRecords");
                Assert.That(localized.Name, Is.EqualTo(originalLocalized.Name));
                Assert.That(localized.Desc, Is.EqualTo(originalLocalized.Desc));
                Assert.That(localized.Suffix, Is.EqualTo("Командир взвода"));
                // Isolate access checks from the test map's lack of an electrical network.
                em.RemoveComponent<ActivatableUIRequiresPowerComponent>(console);
                foreach (var job in prototypes.EnumeratePrototypes<JobPrototype>())
                {
                    var card = em.SpawnEntity("ContractorIDCard", map.GridCoords);
                    access.SetAccessToJob(card, job, false);
                    var tags = readers.FindAccessTags(card);
                    var expected = job.ID is "SeparatistsCommander" or "SeparatistsPrivateSlave";
                    Assert.That(tags.Contains(commanderAccess), Is.EqualTo(expected), job.ID);
                    foreach (var door in doors)
                        Assert.That(readers.IsAllowed(card, door), Is.EqualTo(expected), job.ID);
                    Assert.That(readers.IsAllowed(card, console), Is.EqualTo(expected), job.ID);
                    var open = new ActivatableUIOpenAttemptEvent(card);
                    em.EventBus.RaiseLocalEvent(console, open);
                    Assert.That(open.Cancelled, Is.EqualTo(!expected), job.ID);
                    if (tags.Contains(new ProtoId<AccessLevelPrototype>("Separatists")))
                    {
                        foreach (var door in ordinaryDoors)
                            Assert.That(readers.IsAllowed(card, door), Is.True, job.ID);
                    }
                    em.DeleteEntity(card);
                }

                var remote = em.SpawnEntity("DoorRemoteSeparatists", map.GridCoords);
                foreach (var door in doors)
                    Assert.That(readers.IsAllowed(remote, door), Is.True, "The commander's remote must still work.");
                var emptyCard = em.SpawnEntity("ContractorIDCard", map.GridCoords);
                Assert.That(access.TrySetTags(emptyCard, System.Array.Empty<ProtoId<AccessLevelPrototype>>()), Is.True);
                foreach (var door in doors)
                    Assert.That(readers.IsAllowed(emptyCard, door), Is.False, "An empty ID must not open commander doors.");
                Assert.That(readers.IsAllowed(emptyCard, console), Is.False);
                var onlyCommander = em.SpawnEntity("ContractorIDCard", map.GridCoords);
                Assert.That(access.TrySetTags(onlyCommander, new[] { commanderAccess }), Is.True);
                foreach (var door in doors)
                    Assert.That(readers.IsAllowed(onlyCommander, door), Is.True, "The new access is independent of ordinary Separatist access.");
                Assert.That(readers.IsAllowed(onlyCommander, console), Is.True);

                var glassBase = em.SpawnEntity("AirlockAcorpMiningGlass", map.GridCoords);
                Assert.That(readers.IsAllowed(emptyCard, glassBase), Is.True, "The original glass door must remain unlocked.");
                var originalConsole = em.SpawnEntity("ComputerTabletopStationRecords", map.GridCoords);
                Assert.That(readers.IsAllowed(emptyCard, originalConsole), Is.True);
                Assert.That(em.HasComponent<ActivatableUIRequiresAccessComponent>(originalConsole), Is.False);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}

using System.Linq;
using Content.Server._radiant.Storage;
using Content.Server._Triad.Shipyard;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class IdentityDocumentPersistenceTest
{
    [Test]
    public async Task DocumentsCannotEnterDepositBoxesOrPersistInsideShipSafes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var storage = em.System<SharedStorageSystem>();
            var containers = em.System<SharedContainerSystem>();
            var ids = new[] { "EEPassportZon", "RadiantCredentialDvbSheriff", "RadiantCredentialFleetNavyCaptain" };
            foreach (var boxId in new[] { "SafetyDepositBoxSmall", "SafetyDepositBoxMedium", "SafetyDepositBoxLarge" })
            {
                var box = em.SpawnEntity(boxId, map.GridCoords);
                foreach (var id in ids)
                {
                    var document = em.SpawnEntity(id, map.GridCoords);
                    Assert.That(storage.Insert(box, document, out _, playSound: false), Is.False, $"{boxId}: {id}");
                    em.DeleteEntity(document);
                }
                em.DeleteEntity(box);
            }

            var safe = em.SpawnEntity("PasswordWallSafe", map.GridCoords);
            var contents = em.GetComponent<StorageComponent>(safe).Container;
            foreach (var id in ids)
                Assert.That(containers.Insert(em.SpawnEntity(id, map.GridCoords), contents), Is.True);
            var paper = em.SpawnEntity("Paper", map.GridCoords);
            Assert.That(containers.Insert(paper, contents), Is.True);
            Assert.That(IdentityDocumentPersistence.FindDocuments(em, safe).Count(), Is.EqualTo(3));
            Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid.Owner, out var yaml, out _), Is.True);
            foreach (var id in ids) Assert.That(yaml, Does.Not.Contain(id), "A password safe must not bypass the document policy.");
            Assert.That(IdentityDocumentPersistence.FindDocuments(em, safe).Count(), Is.EqualTo(3),
                "Preparing a ship snapshot must not delete live documents.");
            IdentityDocumentPersistence.RemoveDocuments(em, safe);
            Assert.That(IdentityDocumentPersistence.FindDocuments(em, safe), Is.Empty);
            Assert.That(em.EntityExists(paper), Is.True, "Cleaning old saves must preserve unrelated contents.");
        });
        await pair.CleanReturnAsync();
    }
}

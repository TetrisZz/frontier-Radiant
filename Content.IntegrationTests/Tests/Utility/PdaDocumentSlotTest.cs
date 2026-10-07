using System.Linq;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.PDA;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class PdaDocumentSlotTest
{
    [Test]
    public async Task AllPdasAcceptPassportsAndCredentialsInTheirBookSlot()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, 0, 0);
            var slots = entities.System<ItemSlotsSystem>();
            var documents = prototypes.EnumeratePrototypes<EntityPrototype>()
                .Where(p => !p.Abstract && (p.Components.ContainsKey("Passport") || p.Components.ContainsKey("ServiceCredential")))
                .Select(p => (p.ID, Entity: entities.SpawnEntity(p.ID, coordinates))).ToArray();
            Assert.That(documents.Length, Is.GreaterThanOrEqualTo(19));
            var pdas = prototypes.EnumeratePrototypes<EntityPrototype>()
                // Exclude virtual inventory ID-card placeholders, which are not usable PDAs.
                .Where(p => !p.Abstract && p.Components.ContainsKey("Pda") && p.Components.ContainsKey("CartridgeLoader"))
                .ToArray();
            Assert.That(pdas.Length, Is.GreaterThan(10));
            foreach (var prototype in pdas)
            {
                var uid = entities.SpawnEntity(prototype.ID, coordinates);
                var pda = entities.GetComponent<PdaComponent>(uid);
                Assert.That(pda.BookSlot.HasItem, Is.True, $"{prototype.ID}: keep the starting book");
                Assert.That(slots.TryEject(uid, pda.BookSlot, null, out var book), Is.True, prototype.ID);
                foreach (var (id, document) in documents)
                {
                    Assert.That(slots.TryInsert(uid, PdaComponent.PdaBookSlotId, document, null), Is.True,
                        $"{prototype.ID} should accept {id}");
                    Assert.That(pda.BookSlot.Item, Is.EqualTo(document));
                    Assert.That(slots.TryEject(uid, pda.BookSlot, null, out var ejected), Is.True);
                    Assert.That(ejected, Is.EqualTo(document));
                }
                Assert.That(slots.TryInsert(uid, PdaComponent.PdaBookSlotId, book!.Value, null), Is.True,
                    $"{prototype.ID}: the original book must remain supported");
                var pen = entities.SpawnEntity("Pen", coordinates);
                Assert.That(slots.CanInsert(uid, pen, null, pda.BookSlot, swap: true), Is.False,
                    "The document slot must not accept arbitrary items");
                entities.DeleteEntity(pen);
                entities.DeleteEntity(uid);
            }
            entities.DeleteEntity(map);
        });
    }
}

using System.Linq;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared.Item;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class DepositStorageLayoutTest
{
    [Test]
    public async Task EquipmentFitsTheThreeDepositLayoutsWithoutShrinkingJuggernautsOrEva()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var storage = em.System<SharedStorageSystem>();
            foreach (var (id, price, cells, suits) in new[]
            {
                ("SafetyDepositBoxSmall", 150000, 25, 1),
                ("SafetyDepositBoxMedium", 300000, 41, 2),
                ("SafetyDepositBoxLarge", 400000, 48, 2)
            })
            {
                var box = em.SpawnEntity(id, map.GridCoords);
                Assert.That(em.GetComponent<SafetyDepositBoxComponent>(box).Cost, Is.EqualTo(price));
                Assert.That(em.GetComponent<StorageComponent>(box).Grid.Sum(rect => (rect.Width + 1) * (rect.Height + 1)),
                    Is.EqualTo(cells), id);
                var juggernaut = em.SpawnEntity("ClothingOuterHardsuitJuggernaut", map.GridCoords);
                Assert.That(em.GetComponent<ItemComponent>(juggernaut).Size.Id, Is.EqualTo("Ginormous"));
                Assert.That(storage.Insert(box, juggernaut, out _, playSound: false), Is.False);
                em.DeleteEntity(juggernaut);
                for (var index = 0; index < suits + 1; index++)
                {
                    var suit = em.SpawnEntity("ClothingOuterHardsuitEngineering", map.GridCoords);
                    Assert.That(em.GetComponent<ItemComponent>(suit).Size.Id, Is.EqualTo("Huge"));
                    Assert.That(storage.Insert(box, suit, out _, playSound: false), Is.EqualTo(index < suits),
                        $"{id}: capacity for 4x4 hardsuits");
                    if (index == suits) em.DeleteEntity(suit);
                }
                em.DeleteEntity(box);
            }

            foreach (var id in new[] { "ClothingOuterHardsuitRd", "ClothingOuterHardsuitMercenary", "ClothingOuterHardsuitSyndie" })
            {
                var suit = em.SpawnEntity(id, map.GridCoords);
                var item = em.GetComponent<ItemComponent>(suit);
                Assert.That(item.Size.Id, Is.EqualTo("Huge"), id);
                Assert.That(item.Shape!.Sum(rect => (rect.Width + 1) * (rect.Height + 1)), Is.EqualTo(16), id);
                em.DeleteEntity(suit);
            }
            var eva = em.SpawnEntity("ClothingOuterHardsuitAcorpHardEVA", map.GridCoords);
            Assert.That(em.GetComponent<ItemComponent>(eva).Size.Id, Is.EqualTo("Ginormous"));
            em.DeleteEntity(eva);
        });
        await pair.CleanReturnAsync();
    }
}

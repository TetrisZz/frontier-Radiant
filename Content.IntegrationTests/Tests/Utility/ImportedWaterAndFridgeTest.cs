using System.Collections.Generic;
using Content.Shared._radiant.SmartFridge;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class ImportedWaterAndFridgeTest
{
    [Test]
    public async Task WellHasWaterAndFridgeTracksInsertedAndRemovedItems()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.ResolveDependency<IEntityManager>();
                var appearance = em.System<SharedAppearanceSystem>();
                var containers = em.System<SharedContainerSystem>();
                var solutions = em.System<SharedSolutionContainerSystem>();
                var well = em.SpawnEntity("RadiantWaterWell", map.GridCoords);
                Assert.That(em.HasComponent<DrainableSolutionComponent>(well), Is.True);
                Assert.That(em.HasComponent<SolutionRegenerationComponent>(well), Is.True);
                Assert.That(solutions.TryGetSolution(well, "tank", out _, out var water), Is.True);
                Assert.That(water!.Volume.Float(), Is.EqualTo(500));

                var fridge = em.SpawnEntity("SmartFridgeRadiant", map.GridCoords);
                Assert.That(containers.TryGetContainer(fridge, "smart_fridge_inventory", out var container), Is.True);
                AssertLevel(0);
                var items = new List<EntityUid>();
                for (var i = 1; i <= 76; i++)
                {
                    var item = em.SpawnEntity("Syringe", map.GridCoords);
                    Assert.That(containers.Insert(item, container!), Is.True);
                    items.Add(item);
                    if (i is 1 or 25 or 26 or 75 or 76)
                        AssertLevel(SmartFridgeFillVisualsComponent.FillLevel(i));
                }

                for (var i = items.Count - 1; i >= 0; i--)
                {
                    Assert.That(containers.Remove(items[i], container!), Is.True);
                    if (i is 75 or 25 or 0)
                        AssertLevel(SmartFridgeFillVisualsComponent.FillLevel(i));
                }

                void AssertLevel(int level)
                {
                    Assert.That(appearance.TryGetData<int>(fridge, SmartFridgeFillVisuals.Level, out var actual), Is.True);
                    Assert.That(actual, Is.EqualTo(level));
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}

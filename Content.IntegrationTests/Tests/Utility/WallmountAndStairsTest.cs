using Content.Shared.Cabinet;
using Content.Shared.Construction;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Nutrition.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class WallmountAndStairsTest
{
    [Test]
    public async Task ExtinguisherVariantsAndStairsRecipesWork()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        await server.WaitAssertion(() =>
        {
            var map = entities.System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, 0, 0);
            var appearance = entities.System<SharedAppearanceSystem>();
            var openable = entities.System<OpenableSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            var cabinet = entities.SpawnEntity("ExtinguisherCabinet", coordinates);
            void Check(string expected)
            {
                Assert.That(appearance.TryGetData(cabinet, ItemCabinetVisuals.State, out string state), Is.True);
                Assert.That(state, Is.EqualTo(expected));
            }
            Check("extinguisher_empty_closed");
            Assert.That(openable.TryOpen(cabinet), Is.True);
            Check("extinguisher_empty_open");
            foreach (var (id, opened, closed) in new[]
                     {
                         ("FireExtinguisher", "extinguisher_standard_open", "extinguisher_closed"),
                         ("FireExtinguisherMini", "extinguisher_mini_open", "extinguisher_mini_closed"),
                         ("FireExtinguisherBluespace", "extinguisher_advanced_open", "extinguisher_advanced_closed")
                     })
            {
                var extinguisher = entities.SpawnEntity(id, coordinates);
                Assert.That(slots.TryInsert(cabinet, "ItemCabinet", extinguisher, null), Is.True);
                Check(opened);
                Assert.That(openable.TryClose(cabinet), Is.True);
                Check(closed);
                Assert.That(openable.TryOpen(cabinet), Is.True);
                Check(opened);
                Assert.That(slots.TryEject(cabinet, "ItemCabinet", null, out _), Is.True);
                Check("extinguisher_empty_open");
                entities.DeleteEntity(extinguisher);
            }
            Assert.That(openable.TryClose(cabinet), Is.True);
            Check("extinguisher_empty_closed");
            foreach (var (id, expected) in new[]
                     {
                         ("ExtinguisherCabinetOpen", "extinguisher_empty_open"),
                         ("ExtinguisherCabinetFilled", "extinguisher_closed"),
                         ("ExtinguisherCabinetFilledOpen", "extinguisher_standard_open")
                     })
            {
                var prefilled = entities.SpawnEntity(id, coordinates);
                Assert.That(appearance.TryGetData(prefilled, ItemCabinetVisuals.State, out string state), Is.True);
                Assert.That(state, Is.EqualTo(expected));
            }
            foreach (var id in new[] { "Stairs", "StairStage", "StairWhite", "StairStageWhite", "StairDark", "StairStageDark", "StairWood", "StairStageWood" })
            {
                var recipe = prototypes.Index<ConstructionPrototype>(id);
                Assert.That(recipe.Hide, Is.False);
                var graph = prototypes.Index(recipe.Graph);
                Assert.That(graph.Nodes.ContainsKey(recipe.TargetNode), Is.True, id);
                Assert.That(graph.TryPath(recipe.StartNode, recipe.TargetNode, out _), Is.True, id);
                Assert.That(graph.Nodes[recipe.TargetNode].Entity, Is.Not.Null, id);
            }
            entities.DeleteEntity(map);
        });
    }
}

using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._HL.Shipyard;
using Content.Server._Triad.Shipyard;
using Content.Server.Storage.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Labels;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.Paper;
using Content.Shared.Storage.Components;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ShipSavePaperLabelTest
{
    [TestCase("ChemicalBarrelCarbon")]
    [TestCase("CrateGenericSteel")]
    public async Task LabelPaperStaysAttachedAndCanBeEjectedAfterLoad(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            const string content = "[bold]Личная этикетка[/bold]";
            var em = pair.Server.EntMan;
            var slots = em.System<ItemSlotsSystem>();
            var labels = em.System<LabelSystem>();
            var furniture = em.SpawnEntity(prototype, map.GridCoords);
            Assert.That(em.System<SharedTransformSystem>().AnchorEntity(furniture), Is.True);
            if (slots.TryEject(furniture, LabelSystem.ContainerName, null, out var factoryLabel))
                em.DeleteEntity(factoryLabel);

            var paper = em.SpawnEntity("Paper", map.GridCoords);
            em.System<PaperSystem>().SetContent(paper, content);
            Assert.That(slots.TryInsert(furniture, LabelSystem.ContainerName, paper, null), Is.True);
            var loosePaper = em.SpawnEntity("Paper", map.GridCoords);
            em.System<PaperSystem>().SetContent(loosePaper, "Loose sheet excluded from save");
            if (em.HasComponent<EntityStorageComponent>(furniture))
            {
                var apple = em.SpawnEntity("FoodApple", map.GridCoords);
                Assert.That(em.System<EntityStorageSystem>().Insert(apple, furniture), Is.True);
                Assert.That(em.System<SharedContainerSystem>().IsEntityInContainer(apple), Is.True);
            }

            Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
            Assert.That(yaml, Does.Not.Contain("Loose sheet excluded from save").And.Not.Contain("FoodApple"));
            Assert.That(labels.TryGetLabel<PaperComponent>(furniture, out var liveLabel), Is.True);
            Assert.That(liveLabel!.Value.Owner, Is.EqualTo(paper));

            using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml!, out _));
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "labelled furniture", out var loaded,
                offset: new Vector2(20, 0)), Is.True);
            var restored = em.EntityQuery<MetaDataComponent, TransformComponent>(true)
                .Single(entry => entry.Item2.GridUid == loaded!.Value.Owner && entry.Item1.EntityPrototype?.ID == prototype)
                .Item1.Owner;
            Assert.That(labels.TryGetLabel<PaperComponent>(restored, out var label), Is.True);
            Assert.That(label!.Value.Comp.Content, Is.EqualTo(content));
            Assert.That(em.System<SharedAppearanceSystem>().TryGetData<bool>(restored, PaperLabelVisuals.HasLabel, out var visible), Is.True);
            Assert.That(visible, Is.True);
            Assert.That(slots.TryEject(restored, LabelSystem.ContainerName, null, out var ejected), Is.True);
            Assert.That(ejected, Is.EqualTo(label.Value.Owner));
            Assert.That(labels.TryGetLabel<PaperComponent>(restored, out _), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}

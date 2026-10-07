using Content.Server._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared.Clothing.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class SurgicalDisinfectionTest
{
    [Test]
    public async Task ImprovisedToolsCanBeDisinfected()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, 0, 0);
            var surgery = entities.System<SurgerySystem>();
            var pen = entities.SpawnEntity("Pen", coordinates);
            Assert.That(entities.HasComponent<SurgicalDrillComponent>(pen), Is.True);
            Assert.That(surgery.CanDisinfectItem(pen), Is.True);
            // Also cover improvised prototypes that only carry a specific tool capability.
            entities.RemoveComponent<SurgeryToolComponent>(pen);
            Assert.That(surgery.CanDisinfectItem(pen), Is.True, "A fresh improvised tool must be cleanable before its first operation.");
            var sterility = entities.EnsureComponent<SurgicalItemSterilityComponent>(pen);
            sterility.Dirty = true;
            Assert.That(surgery.CanDisinfectItem(pen), Is.True);
            Assert.That(surgery.CanDisinfectItem(entities.SpawnEntity("Scalpel", coordinates)), Is.True);
            Assert.That(surgery.CanDisinfectItem(entities.SpawnEntity("ShardGlass", coordinates)), Is.True);
            Assert.That(surgery.CanDisinfectItem(entities.SpawnEntity(null, coordinates)), Is.False);
            entities.EnsureComponent<MaskComponent>(pen);
            Assert.That(surgery.CanDisinfectItem(pen), Is.False, "The mask exclusion must still take precedence.");
        });
    }
}

using Content.Shared.Shuttles.Components;
using Robust.Shared.EntitySerialization;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Maps;

[TestFixture]
public sealed class CentCommMapTest
{
    [Test]
    public async Task CentCommLoadsAsMapWithRestrictedFtl()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        await server.WaitAssertion(() =>
        {
            var entities = server.ResolveDependency<IEntityManager>();
            var loader = entities.System<MapLoaderSystem>();
            // Same loader mode and initialization used by LoadCentComm's mapPath.
            Assert.That(loader.TryLoadMap(new ResPath("/Maps/_radiant/CentComm/centcomm_map.yml"),
                out var map, out var grids, DeserializationOptions.Default with { InitializeMaps = true }), Is.True);
            Assert.That(grids, Has.Count.EqualTo(1));
            Assert.That(entities.TryGetComponent<FTLDestinationComponent>(map!.Value.Owner, out var destination), Is.True);
            Assert.That(destination!.RequireCoordinateDisk, Is.True);
        });
    }
}

using System;
using Content.Server._Triad.Shipyard;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.ContentPack;
using Content.Server.GameTicking;
using Content.Shared.CCVar;
using Robust.Shared.Configuration;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

[TestFixture]
public sealed class ShipSaveRecoveryTest
{
    [Test]
    public async Task RoundRestartReleasesDeploymentWithoutRevivingDeletedShips()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, DummyTicker = false, Dirty = true });
        try
        {
            var map = await pair.CreateTestMap();
            AuthenticatedShipFile file = null!;
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                var signing = entities.System<ShipSaveSigningSystem>();
                Assert.That(entities.System<ShipyardGridSaveSystem>()
                    .TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
                file = signing.Sign(yaml!, 0);
                signing.MakeAvailable(file);
                signing.Deploy(file, map.Grid, pair.Player!, "/Exports/round-recovery.yml");
                Assert.That(signing.IsAvailable(file), Is.False);
                pair.Server.ResolveDependency<IConfigurationManager>().SetCVar(CCVars.GameLobbyEnabled, true);
                entities.System<GameTicker>().RestartRound();
                Assert.That(entities.EntityExists(map.Grid), Is.False);
                Assert.That(signing.IsAvailable(file), Is.True, "Round cleanup must preserve recovery");
            });
            var nextMap = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                var signing = entities.System<ShipSaveSigningSystem>();
                signing.Deploy(file, nextMap.Grid, pair.Player!, "/Exports/round-recovery.yml");
                Assert.That(signing.IsAvailable(file), Is.False, "A recovered file must block duplicates again");
                entities.System<SharedMapSystem>().DeleteMap(nextMap.MapId);
                var queuedFile = signing.Sign($"queued: {Guid.NewGuid()}", 0);
                var queued = entities.SpawnEntity(null, MapCoordinates.Nullspace);
                signing.MakeAvailable(queuedFile);
                signing.Deploy(queuedFile, queued, pair.Player!, "/Exports/queued.yml");
                entities.QueueDeleteEntity(queued);
                entities.System<GameTicker>().RestartRound();
                Assert.That(signing.IsAvailable(file), Is.False, "A round restart must not revive a deleted ship");
                Assert.That(signing.IsAvailable(queuedFile), Is.False, "Pending sales and replacement saves must stay revoked");
                TestContext.Out.WriteLine("Ship round restart recovery assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Ship round restart recovery regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }

    [Test]
    public async Task DeploymentIsRuntimeOnlyAndDeletionRevokesSnapshot()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                var signing = entities.System<ShipSaveSigningSystem>();
                var saving = entities.System<ShipyardGridSaveSystem>();
                Assert.That(saving.TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
                var file = signing.Sign(yaml!, 0);
                signing.MakeAvailable(file);
                Assert.That(signing.IsAvailable(file), Is.True);
                signing.Deploy(file, map.Grid, pair.Player!, "/Exports/recovery.yml");
                Assert.That(signing.IsAvailable(file), Is.False);
                var restart = new ShipSaveAvailabilityStore(
                    pair.Server.ResolveDependency<IResourceManager>().UserData, Guid.NewGuid());
                var hash = Convert.ToHexString(file.GetHash());
                Assert.That(restart.IsAvailable(hash), Is.True);
                Assert.That(entities.HasComponent<DeployedShipSaveComponent>(map.Grid), Is.True);
                Assert.That(saving.TryBuildShipSaveYaml(map.Grid, out var deployedYaml, out _), Is.True);
                Assert.That(deployedYaml, Does.Not.Contain("DeployedShipSave"));
                entities.System<SharedMapSystem>().DeleteMap(map.MapId);
                Assert.That(signing.IsAvailable(file), Is.False);
                Assert.That(restart.IsAvailable(hash), Is.False, "Deleted ships must not recover after restart");
                TestContext.Out.WriteLine("Ship recovery lifecycle assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Ship recovery lifecycle regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}

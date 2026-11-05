using System.IO;
using System.Linq;
using Content.Server._Triad.Shipyard;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Shared.Coordinates;
using Content.Shared.Doors.Components;
using Content.Shared.Power;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ShipSavePowerTest
{
    [TestCase(true)]
    [TestCase(false)]
    public async Task LoadedDevicesReconnectAndAirlockMatchesActualPower(bool chargeOnLoad)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        string yaml = null!;
        EntityUid grid = map.Grid.Owner;
        await pair.Server.WaitAssertion(() =>
        {
            var maps = em.System<SharedMapSystem>();
            for (var x = 0; x <= 9; x++)
                maps.SetTile(map.Grid, new Vector2i(x, 0), new Tile(1));
            // Reproduce the load-order failure: an isolated cable starts first and
            // claims the door before the connected cable directly under it exists.
            em.SpawnEntity("CableApcExtension", grid.ToCoordinates());
            em.SpawnEntity("Airlock", grid.ToCoordinates(2, 0));
            for (var x = 2; x <= 9; x++)
                em.SpawnEntity("CableApcExtension", grid.ToCoordinates(x, 0));
            em.SpawnEntity("APCBasic", grid.ToCoordinates(9, 0));
            em.SpawnEntity("Poweredlight", grid.ToCoordinates(5, 0));
            em.SpawnEntity("ServiceTechFab", grid.ToCoordinates(8, 0));
        });
        await pair.Server.WaitRunTicks(10);
        await pair.Server.WaitAssertion(() =>
        {
            CheckPower(true);
            Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(grid, out var snapshot, out _), Is.True);
            yaml = snapshot!;
            em.DeleteEntity(grid);
        });
        await pair.Server.WaitRunTicks(5);
        await pair.Server.WaitAssertion(() =>
        {
            using var reader = new StringReader(yaml);
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "ship power regression", out var loaded), Is.True);
            grid = loaded!.Value.Owner;
            var apc = em.EntityQuery<ApcComponent, TransformComponent>(true)
                .Single(entry => entry.Item2.GridUid == grid).Item1.Owner;
            // Recharge explicitly: ship storage intentionally does not promise stored energy.
            em.System<BatterySystem>().SetCharge(apc, chargeOnLoad ? 50000 : 0);
        });
        await pair.Server.WaitRunTicks(10);
        await pair.Server.WaitAssertion(() =>
        {
            CheckPower(chargeOnLoad);
            var apc = em.EntityQuery<ApcComponent, TransformComponent>(true)
                .Single(entry => entry.Item2.GridUid == grid).Item1.Owner;
            em.System<BatterySystem>().SetCharge(apc, 50000);
        });
        await pair.Server.WaitRunTicks(10);
        await pair.Server.WaitAssertion(() =>
        {
            CheckPower(true);
            foreach (var entry in em.EntityQuery<ApcComponent, TransformComponent>(true)
                         .Where(entry => entry.Item2.GridUid == grid).ToArray())
                em.System<BatterySystem>().SetCharge(entry.Item1.Owner, 0);
        });
        await pair.Server.WaitRunTicks(10);
        await pair.Server.WaitAssertion(() => CheckPower(false));
        await pair.CleanReturnAsync();

        void CheckPower(bool expected)
        {
            var receivers = em.EntityQuery<ApcPowerReceiverComponent, TransformComponent>(true)
                .Where(entry => entry.Item2.GridUid == grid).Select(entry => entry.Item1).ToArray();
            Assert.That(receivers.Length, Is.EqualTo(3));
            foreach (var receiver in receivers)
            {
                var uid = receiver.Owner;
                var prototype = em.GetComponent<MetaDataComponent>(uid).EntityPrototype!.ID;
                TestContext.Out.WriteLine($"SHIP_POWER {prototype}: powered={receiver.Powered}, receiving={receiver.PowerReceived}, provider={receiver.Provider != null}");
                Assert.That(receiver.Provider, Is.Not.Null, prototype + ": the loaded cable connection must be restored.");
                Assert.That(em.GetComponent<TransformComponent>(receiver.Provider!.Owner).LocalPosition,
                    Is.EqualTo(em.GetComponent<TransformComponent>(uid).LocalPosition),
                    prototype + ": a cable directly under the device must win over a cable which happened to start earlier.");
                Assert.That(receiver.Powered, Is.EqualTo(expected), prototype + ": actual power state");
                var appearance = em.System<SharedAppearanceSystem>();
                Assert.That(appearance.TryGetData<bool>(uid, PowerDeviceVisuals.Powered, out var visual), Is.True, prototype);
                Assert.That(visual, Is.EqualTo(expected), prototype + ": power indication");
                if (em.TryGetComponent<AirlockComponent>(uid, out var airlock))
                    Assert.That(airlock.Powered, Is.EqualTo(expected), "Airlock operation must agree with power indication and examination.");
            }
        }
    }
}

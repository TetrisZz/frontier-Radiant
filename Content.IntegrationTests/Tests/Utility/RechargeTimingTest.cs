using Content.Shared.Storage.EntitySystems;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Events;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class RechargeTimingTest
{
    [Test]
    public async Task MechAcceleratorRechargeMatchesModifiedFireRateAndEarlyTriggerDoesNotDelayIt()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            var uid = em.SpawnEntity("WeaponMechIndustrialKineticAccelerator", map.GridCoords);
            var gun = em.GetComponent<GunComponent>(uid);
            var recharge = em.GetComponent<RechargeBasicEntityAmmoComponent>(uid);
            var ammo = em.GetComponent<BasicEntityAmmoProviderComponent>(uid);
            Assert.That(recharge.UseGunFireRate, Is.True);
            foreach (var rate in new[] { 1.5f, 3f })
            {
                gun.FireRateModified = rate;
                em.System<SharedGunSystem>().UpdateBasicEntityAmmoCount(uid, 1);
                // Exact equality previously left the old charge deadline in place.
                recharge.NextCharge = timing.CurTime;
                var take = new TakeAmmoEvent(2, new(), map.GridCoords, null, true);
                em.EventBus.RaiseLocalEvent(uid, take);
                Assert.That(take.Ammo, Has.Count.EqualTo(1));
                Assert.That(ammo.Count, Is.Zero);
                Assert.That((recharge.NextCharge!.Value - timing.CurTime).TotalSeconds,
                    Is.EqualTo(1d / rate).Within(0.00001));
                var nextCharge = recharge.NextCharge;
                gun.NextFire = timing.CurTime;
                em.System<SharedGunSystem>().AttemptShoot(uid, gun);
                Assert.That(gun.NextFire, Is.EqualTo(timing.CurTime), "A charging weapon must not acquire an empty-fire penalty.");
                Assert.That(recharge.NextCharge, Is.EqualTo(nextCharge));
                foreach (var (projectile, _) in take.Ammo)
                    if (projectile is { } entity)
                        em.DeleteEntity(entity);
            }

            var standard = em.SpawnEntity("WeaponProtoKineticAccelerator", map.GridCoords);
            var standardRecharge = em.GetComponent<RechargeBasicEntityAmmoComponent>(standard);
            Assert.That(standardRecharge.UseGunFireRate, Is.False, "Other accelerators retain their own configured recharge timing.");
        });
        await pair.CleanReturnAsync();
    }

    [TestCase("NFPouchUtility")]
    [TestCase("NFPouchAcorp")]
    public async Task PouchesAllowPillCanistersButStillRejectOtherStorage(string prototype)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var storage = em.System<SharedStorageSystem>();
            var pouch = em.SpawnEntity(prototype, map.GridCoords);
            var pills = em.SpawnEntity("PillCanisterBicaridine", map.GridCoords);
            Assert.That(storage.Insert(pouch, pills, out _, playSound: false), Is.True);
            var otherPouch = em.SpawnEntity("NFPouchUtility", map.GridCoords);
            Assert.That(storage.CanInsert(pouch, otherPouch, out _, ignoreLocation: true), Is.False);
        });
        await pair.CleanReturnAsync();
    }
}

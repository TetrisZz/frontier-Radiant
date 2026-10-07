using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class HeavyWoundedTest
{
    [Test]
    public async Task EntryRollIsLatchedAndDeathEndsFightForLife()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var em = server.ResolveDependency<IEntityManager>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            var uid = em.SpawnEntity("MobHuman", new EntityCoordinates(map, 0, 0));
            var heavy = em.GetComponent<HeavyWoundedComponent>(uid);
            var mob = em.GetComponent<MobStateComponent>(uid);
            var damage = em.GetComponent<DamageableComponent>(uid);
            var states = em.System<MobStateSystem>();
            void SetDamage(int amount) => em.System<DamageableSystem>().SetDamage(uid, damage,
                new DamageSpecifier { DamageDict = new() { ["Blunt"] = FixedPoint2.New(amount) } });

            Assert.That(heavy.SkipChance, Is.EqualTo(0.25f));
            heavy.SkipChance = 0;
            SetDamage(100);
            Assert.That(heavy.Active, Is.True);
            Assert.That(mob.CurrentState, Is.EqualTo(MobState.Alive));
            Assert.That(heavy.EntryRolled, Is.True);
            heavy.SkipChance = 1;
            SetDamage(105);
            for (var i = 0; i < 5; i++)
                states.UpdateMobState(uid);
            Assert.That(mob.CurrentState, Is.EqualTo(MobState.Alive), "No reroll within an injury episode.");

            SetDamage(99);
            Assert.That(heavy.EntryRolled, Is.False);
            SetDamage(100);
            Assert.That(heavy.Skipped, Is.True);
            Assert.That(heavy.Active, Is.False);
            Assert.That(mob.CurrentState, Is.EqualTo(MobState.Critical));
            states.UpdateMobState(uid);
            Assert.That(mob.CurrentState, Is.EqualTo(MobState.Critical), "Do not recover immediately at exactly 100.");
            SetDamage(99);
            Assert.That(mob.CurrentState, Is.EqualTo(MobState.Alive));
            Assert.That(heavy.Skipped, Is.False);

            SetDamage(120);
            Assert.That(mob.CurrentState, Is.EqualTo(MobState.Critical));
            em.EnsureComponent<MobStateActionsComponent>(uid);
            var action = new FightForLifeEvent();
            em.EventBus.RaiseLocalEvent(uid, action);
            Assert.That(action.Handled, Is.True);
            Assert.That(em.HasComponent<FightingForLifeComponent>(uid), Is.True);
            Assert.That(em.GetComponent<StandingStateComponent>(uid).Standing, Is.True);
            // Retain the knockdown component: its removal during death used to stand the corpse up.
            em.EnsureComponent<KnockedDownComponent>(uid);
            SetDamage(1000);
            Assert.That(mob.CurrentState, Is.EqualTo(MobState.Dead));
            Assert.That(em.HasComponent<FightingForLifeComponent>(uid), Is.False);
            Assert.That(em.GetComponent<StandingStateComponent>(uid).Standing, Is.False);
            Assert.That(em.System<StandingStateSystem>().Stand(uid), Is.False);
            em.System<SharedStunSystem>().ForceStandUp(uid);
            Assert.That(em.GetComponent<StandingStateComponent>(uid).Standing, Is.False);
        });
    }
}

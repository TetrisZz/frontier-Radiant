using System.Linq;
using Content.Server.Medical;
using Content.Shared.Actions;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Standing;
using Content.Shared.Stunnable;
using Content.Shared.Item.ItemToggle;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class HeavyWoundedTest
{
    [Test]
    public async Task EntryRollIsLatchedAndDeathEndsFightForLife()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var server = pair.Server;
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
            var actions = em.System<SharedActionsSystem>();
            var action = actions.GetActions(uid).Single(entry => em.GetComponent<MetaDataComponent>(entry).EntityPrototype?.ID == "ActionFightForLife");
            Assert.That(actions.ValidAction(action), Is.False);
            Assert.That((action.Comp.Cooldown!.Value.End - action.Comp.Cooldown.Value.Start).TotalSeconds, Is.InRange(35, 149));
            actions.ClearCooldown(action);
            Assert.That(actions.ValidAction(action), Is.True);
            actions.PerformAction(uid, action);
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
        await pair.CleanReturnAsync();
    }

    [TestCase("Defibrillator")]
    [TestCase("DefibrillatorSyndicate")]
    public async Task InitialFightDelayAllowsHealingAndDefibrillation(string defibrillator)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid patient = default;
        await pair.Server.WaitAssertion(() =>
        {
            patient = em.SpawnEntity("MobHuman", map.GridCoords);
            var doctor = em.SpawnEntity("MobHumanDummy", map.GridCoords);
            var damage = em.GetComponent<DamageableComponent>(patient);
            var health = em.System<DamageableSystem>();
            var states = em.System<MobStateSystem>();
            var actions = em.System<SharedActionsSystem>();
            health.SetDamage(patient, damage, new DamageSpecifier { DamageDict = new() { ["Blunt"] = 120 } });
            Assert.That(states.IsCritical(patient), Is.True);
            var firstAction = actions.GetActions(patient)
                .Single(entry => em.GetComponent<MetaDataComponent>(entry).EntityPrototype?.ID == "ActionFightForLife");
            Assert.That(actions.ValidAction(firstAction), Is.False);
            health.TryChangeDamage(patient, new DamageSpecifier { DamageDict = new() { ["Blunt"] = -30 } },
                ignoreResistances: true, interruptsDoAfters: false);
            Assert.That(states.IsAlive(patient), Is.True, "Healing must end critical condition during the initial delay.");

            health.SetDamage(patient, damage, new DamageSpecifier { DamageDict = new() { ["Blunt"] = 250 } });
            Assert.That(states.IsDead(patient), Is.True);
            health.SetDamage(patient, damage, new DamageSpecifier { DamageDict = new() { ["Blunt"] = 150, ["Asphyxiation"] = 40 } });
            Assert.That(states.IsDead(patient), Is.True);
            var defib = em.SpawnEntity(defibrillator, map.GridCoords);
            Assert.That(em.System<ItemToggleSystem>().TryActivate(defib), Is.True);
            var defibs = em.System<DefibrillatorSystem>();
            Assert.That(defibs.CanZap(defib, patient, doctor), Is.True);
            defibs.Zap(defib, patient, doctor);
            Assert.That(states.IsCritical(patient), Is.True, "A successful shock must leave the patient in critical condition.");
            Assert.That(damage.TotalDamage.Float(), Is.LessThan(200));
            var revivedAction = actions.GetActions(patient)
                .Single(entry => em.GetComponent<MetaDataComponent>(entry).EntityPrototype?.ID == "ActionFightForLife");
            Assert.That(actions.ValidAction(revivedAction), Is.False);
            Assert.That((revivedAction.Comp.Cooldown!.Value.End - revivedAction.Comp.Cooldown.Value.Start).TotalSeconds, Is.InRange(35, 149));
            Assert.That(em.HasComponent<FightingForLifeComponent>(patient), Is.False);
            for (var i = 0; i < 5; i++)
                states.UpdateMobState(patient);
            Assert.That(states.IsCritical(patient), Is.True, "The action delay must not send the patient back to death.");
            health.SetDamage(patient, damage, new DamageSpecifier { DamageDict = new() { ["Blunt"] = 90 } });
            Assert.That(states.IsAlive(patient), Is.True, "Treatment must also work after resuscitation.");
        });
        await pair.CleanReturnAsync();
    }
}

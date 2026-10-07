using Content.Server._radiant.Addictions;
using Content.Shared._radiant.Addictions;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Preferences;
using Content.Shared.Rejuvenate;
using Content.Shared.Traits;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class AddictionTraitsTest
{
    [Test]
    public async Task RecoveryAndPermanentHabits()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, 0, 0);
            var system = entities.System<AddictionSystem>();
            var audioLength = entities.System<Robust.Shared.Audio.Systems.SharedAudioSystem>()
                .GetAudioLength(new Robust.Shared.Audio.ResolvedPathSpecifier("/Audio/_radiant/Addictions/heartbeat.ogg"));
            Assert.That(audioLength.TotalSeconds, Is.EqualTo(3).Within(.01));
            foreach (var (trait, group, grace) in new[]
                     {
                         ("RadiantRecoveringAddict", "nicotine", 300),
                         ("RadiantRecoveringAlcoholic", "alcohol", 480),
                     })
            {
                var patient = entities.SpawnEntity("MobHuman", coordinates);
                entities.AddComponents(patient, prototypes.Index<TraitPrototype>(trait).Components, false);
                var recovery = entities.GetComponent<AddictionComponent>(patient).Groups[group];
                Assert.That(recovery.Dependence, Is.EqualTo(40));
                Assert.That(recovery.Tolerance, Is.EqualTo(10));
                Assert.That(recovery.Intoxication, Is.Zero);
                Assert.That(recovery.SecondsWithoutDose, Is.EqualTo(grace));
                Assert.That(entities.HasComponent<PermanentHabitComponent>(patient), Is.False);
            }

            foreach (var (trait, group, reagent) in new[]
                     {
                         ("RadiantSmoker", "nicotine", "Nicotine"),
                         ("RadiantHabitualDrinker", "alcohol", "Ethanol"),
                         ("RadiantHabitualDrugUser", "narcotics", "THC"),
                     })
            {
                var patient = entities.SpawnEntity("MobHuman", coordinates);
                entities.AddComponents(patient, prototypes.Index<TraitPrototype>(trait).Components, false);
                var habit = entities.GetComponent<PermanentHabitComponent>(patient);
                Assert.That(habit.Group, Is.EqualTo(group));
                Assert.That(entities.HasComponent<AddictionComponent>(patient), Is.False);
                habit.SecondsWithoutDose = 3600;
                habit.MessageTimer = 180;
                system.Update(1);
                Assert.That(habit.SecondsWithoutDose, Is.GreaterThan(3600));
                Assert.That(entities.HasComponent<WithdrawalVisualsComponent>(patient), Is.False);
                Assert.That(entities.HasComponent<AddictionComponent>(patient), Is.False,
                    "A habit must not create illness merely through abstinence.");

                system.RegisterDose(patient, prototypes.Index<ReagentPrototype>(reagent), 1);
                Assert.That(habit.SecondsWithoutDose, Is.Zero);
                var acquired = entities.GetComponent<AddictionComponent>(patient);
                Assert.That(acquired.Groups[group].Dependence, Is.GreaterThan(0),
                    "Habit traits do not grant immunity to acquired dependence.");
                var rejuvenate = new RejuvenateEvent();
                entities.EventBus.RaiseLocalEvent(patient, rejuvenate);
                Assert.That(entities.HasComponent<PermanentHabitComponent>(patient), Is.True);
                Assert.That(acquired.Groups, Is.Empty);
                habit.SecondsWithoutDose = 3600;
                system.Update(1);
                Assert.That(entities.GetComponent<PermanentHabitComponent>(patient).Group, Is.EqualTo(group));
            }

            Assert.That(prototypes.HasIndex<TraitPrototype>("RadiantAddictionProne"), Is.False);
            Assert.That(prototypes.HasIndex<TraitPrototype>("RadiantAddictionResistant"), Is.False);
            var profile = new HumanoidCharacterProfile()
                .WithTraitPreference("RadiantSmoker", prototypes)
                .WithTraitPreference("RadiantRecoveringAddict", prototypes)
                .WithTraitPreference("RadiantHabitualDrinker", prototypes);
            Assert.That(profile.TraitPreferences.Contains("RadiantSmoker"), Is.True);
            Assert.That(profile.TraitPreferences.Contains("RadiantRecoveringAddict"), Is.False);
            Assert.That(profile.TraitPreferences.Contains("RadiantHabitualDrinker"), Is.False);
            Assert.That(profile.SkillPointsSpent, Is.Zero);
        });
    }
}

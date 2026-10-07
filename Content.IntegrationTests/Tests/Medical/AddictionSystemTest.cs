using Content.Server._radiant.Addictions;
using Content.Shared._radiant;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DetailExaminable;
using Content.Shared.EntityEffects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class AddictionSystemTest
{
    [Test]
    public async Task GroupsConsentAndRoundLocalState()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        EntityUid exposed = default;
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            var coordinates = new EntityCoordinates(map, 0, 0);
            var patient = entities.SpawnEntity("MobHuman", coordinates);
            var system = entities.System<AddictionSystem>();
            foreach (var id in new[] { "Nicotine", "Ethanol", "Stimulants", "THC", "Aphrodisiac", "Excitement" })
                Assert.That(system.FindGroup(prototypes.Index<ReagentPrototype>(id)), Is.Not.Null, id);
            Assert.That(system.FindGroup(prototypes.Index<ReagentPrototype>("Water")), Is.Null);
            var detail = entities.EnsureComponent<DetailExaminableComponent>(patient);
            detail.ERPStatus = EnumERPStatus.NO;
            system.RegisterDose(patient, prototypes.Index<ReagentPrototype>("Aphrodisiac"), 5);
            Assert.That(entities.HasComponent<AddictionComponent>(patient), Is.False);
            detail.ERPStatus = EnumERPStatus.FULL;
            system.RegisterDose(patient, prototypes.Index<ReagentPrototype>("Aphrodisiac"), 5);
            system.RegisterDose(patient, prototypes.Index<ReagentPrototype>("Excitement"), 5);
            var component = entities.GetComponent<AddictionComponent>(patient);
            Assert.That(component.Groups.Count, Is.EqualTo(2));
            var stimulant = prototypes.Index<ReagentPrototype>("Stimulants");
            system.RegisterDose(patient, stimulant, 5);
            system.RegisterDose(patient, prototypes.Index<ReagentPrototype>("Ephedrine"), 5);
            Assert.That(component.Groups.Count, Is.EqualTo(3), "Stimulants share a group.");
            component.Groups["stimulants"].Tolerance = 100;
            var args = new EntityEffectReagentArgs(patient, entities, null, null, 1, stimulant, null, 1);
            system.SetTolerance(args, stimulant, "Narcotic");
            Assert.That(args.AddictionHealing, Is.EqualTo(.55f).Within(.001));
            Assert.That(args.AddictionSlowdown, Is.EqualTo(.55f).Within(.001));
            var toxic = new EntityEffectReagentArgs(patient, entities, null, null, 1, stimulant, null, 1);
            system.SetTolerance(toxic, stimulant, "Poison");
            Assert.That(toxic.AddictionDamage, Is.EqualTo(.55f).Within(.001));
            // Verify effect execution, not just the multipliers passed to it.
            var damage = entities.GetComponent<Content.Shared.Damage.DamageableComponent>(patient);
            var before = damage.Damage.DamageDict["Blunt"];
            var health = new Content.Shared.EntityEffects.Effects.HealthChange
            {
                Damage = new Content.Shared.Damage.DamageSpecifier
                {
                    DamageDict = new() { ["Blunt"] = 10 },
                },
            };
            health.Effect(toxic);
            Assert.That((damage.Damage.DamageDict["Blunt"] - before).Float(), Is.EqualTo(10).Within(.01),
                "Overdose damage is unaffected unless an effect explicitly opts in.");
            before = damage.Damage.DamageDict["Blunt"];
            health.ToleranceAffectsDamage = true;
            health.Effect(toxic);
            Assert.That((damage.Damage.DamageDict["Blunt"] - before).Float(), Is.EqualTo(5.5f).Within(.01));
            before = damage.Damage.DamageDict["Blunt"];
            health.Damage.DamageDict["Blunt"] = -10;
            health.Effect(args);
            Assert.That((before - damage.Damage.DamageDict["Blunt"]).Float(), Is.EqualTo(5.5f).Within(.01));
            var movement = new Content.Shared.EntityEffects.Effects.MovespeedModifier
            {
                WalkSpeedModifier = .5f,
                SprintSpeedModifier = 1.5f,
            };
            movement.Effect(args);
            var speed = entities.GetComponent<Content.Shared.Chemistry.Components.MovespeedModifierMetabolismComponent>(patient);
            Assert.That(speed.WalkSpeedModifier, Is.EqualTo(.725f).Within(.001));
            Assert.That(speed.SprintSpeedModifier, Is.EqualTo(1.5f), "Speed boosts remain unchanged.");

            var withdrawal = component.Groups["stimulants"];
            withdrawal.Dependence = 80;
            withdrawal.SecondsWithoutDose = 1000;
            system.Update(1);
            Assert.That(entities.GetComponent<Content.Shared._radiant.Addictions.WithdrawalVisualsComponent>(patient).Intensity,
                Is.GreaterThan(0));
            system.RegisterDose(patient, stimulant, 1);
            Assert.That(entities.GetComponent<Content.Shared._radiant.Addictions.WithdrawalVisualsComponent>(patient).Intensity,
                Is.Zero, "A new dose clears withdrawal visuals, not tolerance.");
            var fresh = entities.SpawnEntity("MobHuman", coordinates);
            Assert.That(entities.HasComponent<AddictionComponent>(fresh), Is.False);
            exposed = fresh;
            var solutions = entities.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(fresh, BloodstreamComponent.DefaultChemicalsSolutionName, out var solution), Is.True);
            solutions.TryAddReagent(solution!.Value, "Nicotine", 1);
        });
        // Exercise the real organ metabolism hook, rather than only RegisterDose.
        await server.WaitRunTicks(180);
        await server.WaitAssertion(() =>
        {
            Assert.That(entities.TryGetComponent<AddictionComponent>(exposed, out var state), Is.True);
            Assert.That(state!.Groups.ContainsKey("nicotine"), Is.True);
            Assert.That(state.Groups["nicotine"].Tolerance, Is.GreaterThan(0));
            Assert.That(state.Groups["nicotine"].Dependence, Is.LessThan(20));
        });
    }
}

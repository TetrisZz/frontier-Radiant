using System;
using System.Linq;
using Content.Shared._radiant.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared.Bed.Sleep;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Damage;
using Content.Shared.Drowsiness;
using Content.Shared.EntityEffects;
using Content.Shared.FixedPoint;
using Content.Shared.StatusEffectNew;
using Content.Shared.Traits.Assorted;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class MorphineSurgeryTest
{
    [TestPrototypes]
    private const string Prototypes = @"
- type: entity
  id: MorphineTestPainStep
  components:
  - type: SurgeryStepEmoteEffect
    emote: Scream
";

    [Test]
    public async Task MorphineCanBeMixedMetabolizedAndUsedForSurgeryWithoutPermanentNumbness()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var em = pair.Server.EntMan;
            EntityUid metabolizingPatient = default;
            await pair.Server.WaitAssertion(() =>
            {
                var prototypes = pair.Server.ProtoMan;
                var reagent = prototypes.Index<ReagentPrototype>("Morphine");
                var effects = reagent.Metabolisms!.Single().Value;
                Assert.That(effects.MetabolismRate, Is.EqualTo(FixedPoint2.New(0.1)));
                foreach (var effect in effects.Effects)
                    effect.GuidebookEffectDescription(prototypes, em.EntitySysManager);
                var inventory = prototypes.Index<VendingMachineInventoryPrototype>("NanoMedPlusInventory");
                Assert.That(inventory.StartingInventory["ChemistryBottleMorphine"], Is.EqualTo(3));

                var solutions = em.System<SharedSolutionContainerSystem>();
                var bottle = em.SpawnEntity("ChemistryBottleMorphine", map.GridCoords);
                Assert.That(solutions.TryGetSolution(bottle, "drink", out _, out var contents), Is.True);
                Assert.That(contents!.GetTotalPrototypeQuantity("Morphine"), Is.EqualTo(FixedPoint2.New(30)));
                var beaker = em.SpawnEntity("Beaker", map.GridCoords);
                Assert.That(solutions.TryGetSolution(beaker, "beaker", out var mixture, out var mixed), Is.True);
                solutions.TryAddReagent(mixture!.Value, "Phenol", 2);
                solutions.TryAddReagent(mixture.Value, "Nitrogen", 1);
                solutions.TryAddReagent(mixture.Value, "Oxygen", 1);
                solutions.UpdateChemicals(mixture.Value, true);
                Assert.That(mixed!.GetTotalPrototypeQuantity("Morphine"), Is.EqualTo(FixedPoint2.New(4)));
                Assert.That(mixed.Volume, Is.EqualTo(FixedPoint2.New(4)));

                var statuses = em.System<StatusEffectsSystem>();
                var sleeping = em.System<SleepingSystem>();
                var surgeon = em.SpawnEntity("MobHuman", map.GridCoords);
                var step = em.SpawnEntity("MorphineTestPainStep", map.GridCoords);
                var patient = em.SpawnEntity("MobHuman", map.GridCoords);
                ApplyDose(patient, 15);
                Assert.That(statuses.HasEffectComp<SurgicalAnalgesiaStatusEffectComponent>(patient), Is.True);
                Assert.That(statuses.HasEffectComp<DrowsinessStatusEffectComponent>(patient), Is.True);
                Assert.That(em.HasComponent<SleepingComponent>(patient), Is.False, "Morphine does not require unconsciousness.");
                Assert.That(em.HasComponent<PainNumbnessComponent>(patient), Is.False, "Do not grant the permanent trait or hide health alerts.");
                Assert.That(em.GetComponent<DamageableComponent>(patient).Damage.DamageDict["Asphyxiation"], Is.EqualTo(FixedPoint2.Zero));
                Assert.That(sleeping.TrySleeping(patient), Is.True);
                PainStep(patient);
                Assert.That(em.HasComponent<SleepingComponent>(patient), Is.True, "Surgery must not wake a patient under analgesia.");

                var untreated = em.SpawnEntity("MobHuman", map.GridCoords);
                Assert.That(sleeping.TrySleeping(untreated), Is.True);
                PainStep(untreated);
                Assert.That(em.HasComponent<SleepingComponent>(untreated), Is.False, "Ordinary sleep still does not protect against surgery.");
                var overdose = em.SpawnEntity("MobHuman", map.GridCoords);
                ApplyDose(overdose, 16);
                Assert.That(em.GetComponent<DamageableComponent>(overdose).Damage.DamageDict["Asphyxiation"], Is.EqualTo(FixedPoint2.New(2)));

                metabolizingPatient = em.SpawnEntity("MobHuman", map.GridCoords);
                Assert.That(solutions.TryGetSolution(metabolizingPatient, BloodstreamComponent.DefaultChemicalsSolutionName,
                    out var bloodstream), Is.True);
                Assert.That(solutions.TryAddReagent(bloodstream!.Value, "Morphine", 15), Is.True);

                void ApplyDose(EntityUid target, int amount)
                {
                    var source = new Solution();
                    source.AddReagent("Morphine", amount);
                    var args = new EntityEffectReagentArgs(target, em, null, source, amount, reagent, null, 1);
                    foreach (var effect in effects.Effects)
                    {
                        if (effect.ShouldApply(args))
                            effect.Effect(args);
                    }
                }

                void PainStep(EntityUid target)
                {
                    var pain = new SurgeryStepEvent(surgeon, target, target, [])
                    {
                        StepProto = "MorphineTestPainStep",
                        SurgeryProto = "SurgeryOpenIncision",
                    };
                    em.EventBus.RaiseLocalEvent(step, ref pain);
                }
            });
            await pair.Server.WaitRunTicks(120);
            await pair.Server.WaitAssertion(() =>
            {
                var statuses = em.System<StatusEffectsSystem>();
                Assert.That(statuses.HasEffectComp<SurgicalAnalgesiaStatusEffectComponent>(metabolizingPatient), Is.True,
                    "Real organ metabolism must apply analgesia, not just direct effect execution.");
                var solutions = em.System<SharedSolutionContainerSystem>();
                Assert.That(solutions.TryGetSolution(metabolizingPatient, BloodstreamComponent.DefaultChemicalsSolutionName,
                    out var bloodstream, out var chemicals), Is.True);
                Assert.That(chemicals!.GetTotalPrototypeQuantity("Morphine"), Is.LessThan(FixedPoint2.New(15)));
                solutions.RemoveAllSolution(bloodstream!.Value);
                Assert.That(statuses.TrySetStatusEffectDuration(metabolizingPatient, "StatusEffectSurgicalAnalgesia", TimeSpan.FromMilliseconds(1)), Is.True);
            });
            await pair.Server.WaitRunTicks(2);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(em.System<StatusEffectsSystem>().HasEffectComp<SurgicalAnalgesiaStatusEffectComponent>(metabolizingPatient), Is.False);
                Assert.That(em.HasComponent<PainNumbnessComponent>(metabolizingPatient), Is.False);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}

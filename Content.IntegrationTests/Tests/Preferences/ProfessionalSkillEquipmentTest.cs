using System;
using Content.Server.Botany.Components;
using Content.Server.Hands.Systems;
using Content.Server.Kitchen.Components;
using Content.Server.Kitchen.EntitySystems;
using Content.Server.Power.Components;
using Content.Shared._radiant.Skills;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mech.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Preferences;

public sealed class ProfessionalSkillEquipmentTest
{
    [TestPrototypes]
    private const string SurgeryPrototypes = @"
- type: entity
  id: SkillBasicSurgeryTestStep
  components:
  - type: SurgeryStep

- type: entity
  id: SkillAdvancedSurgeryTestStep
  components:
  - type: SurgeryStep
  - type: SurgeryStepOrganExtract
";

    [Test]
    public async Task EquipmentGatesAndNoviceMedipensWorkThroughActualInteractions()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var em = pair.Server.EntMan;
            EntityUid user = default;
            EntityUid other = default;
            EntityUid pen = default;
            await pair.Server.WaitAssertion(() =>
            {
                user = em.SpawnEntity("MobHuman", map.GridCoords);
                other = em.SpawnEntity("MobHuman", map.GridCoords);
                var ranks = em.AddComponent<ProfessionalSkillsComponent>(user);
                var skills = em.System<SharedProfessionalSkillsSystem>();
                var requirements = new[]
                {
                    ("MiningDrill", ProfessionalSkill.Salvage, 1, SkillAction.Use),
                    ("MiningDrillDiamond", ProfessionalSkill.Salvage, 1, SkillAction.Use),
                    ("AdvancedMineralScannerUnpowered", ProfessionalSkill.Salvage, 1, SkillAction.Use),
                    ("OreBagOfHolding", ProfessionalSkill.Salvage, 0, SkillAction.Use),
                    ("OreProcessorCivilian", ProfessionalSkill.Salvage, 0, SkillAction.Interface),
                    ("OreProcessor", ProfessionalSkill.Salvage, 2, SkillAction.Interface),
                    ("OreProcessorIndustrial", ProfessionalSkill.Salvage, 3, SkillAction.Interface),
                    ("Hypospray", ProfessionalSkill.Medicine, 2, SkillAction.Use),
                    ("HypoMini", ProfessionalSkill.Medicine, 2, SkillAction.Use),
                    ("HyposprayAcorp", ProfessionalSkill.Medicine, 2, SkillAction.Use),
                    ("ChemDispenser", ProfessionalSkill.Medicine, 4, SkillAction.Interface),
                    ("HydroponicsTrayEmpty", ProfessionalSkill.Botany, 1, SkillAction.Interact),
                    ("hydroponicsSoil", ProfessionalSkill.Botany, 1, SkillAction.Interact),
                    ("SeedExtractor", ProfessionalSkill.Botany, 2, SkillAction.Interact),
                    ("AnomalyScanner", ProfessionalSkill.Science, 3, SkillAction.Use),
                    ("ComputerAnalysisConsole", ProfessionalSkill.Science, 2, SkillAction.Interface),
                    ("MachineAPE", ProfessionalSkill.Science, 0, SkillAction.Interface),
                    ("ContainmentFieldGenerator", ProfessionalSkill.Engineering, 3, SkillAction.Interact),
                    ("TeslaGenerator", ProfessionalSkill.Engineering, 3, SkillAction.Anchor),
                    ("ParticleAcceleratorControlBox", ProfessionalSkill.Engineering, 3, SkillAction.Interface),
                };
                foreach (var (id, skill, required, action) in requirements)
                {
                    var equipment = em.SpawnEntity(id, map.GridCoords);
                    for (var level = 0; level <= ProfessionalSkillRules.Maximum(skill); level++)
                    {
                        Array.Clear(ranks.Levels);
                        ranks.Levels[(int) skill] = level;
                        Assert.That(skills.CanUse(user, equipment, action, false), Is.EqualTo(level >= required),
                            $"{id}, {action}, {skill} {level}");
                        if (action == SkillAction.Interface
                            && skills.Requirement(equipment, action) is { UiMessages.Count: 0 }
                            && em.TryGetComponent<Content.Shared.UserInterface.ActivatableUIComponent>(equipment, out var activation)
                            && activation.Key is { } key)
                        {
                            var opening = new BoundUserInterfaceMessageAttempt(user, equipment, key, new OpenBoundInterfaceMessage());
                            em.EventBus.RaiseEvent(EventSource.Local, opening);
                            Assert.That(opening.Cancelled, Is.EqualTo(level < required), $"{id}: opening at {level}");
                            var closing = new BoundUserInterfaceMessageAttempt(user, equipment, key, new CloseBoundInterfaceMessage());
                            em.EventBus.RaiseEvent(EventSource.Local, closing);
                            Assert.That(closing.Cancelled, Is.False, "Closing is always permitted.");
                        }
                    }
                    em.DeleteEntity(equipment);
                }

                var engine = em.SpawnEntity("AmeControllerUnanchored", map.GridCoords);
                var fuel = em.SpawnEntity("AmeJar", map.GridCoords);
                var slots = em.System<Content.Shared.Containers.ItemSlots.ItemSlotsSystem>();
                var controller = em.GetComponent<Content.Server.Ame.Components.AmeControllerComponent>(engine);
                ranks.Levels[(int) ProfessionalSkill.Engineering] = 0;
                Assert.That(slots.TryInsert(engine, controller.FuelSlot, fuel, user), Is.False);
                ranks.Levels[(int) ProfessionalSkill.Engineering] = 1;
                Assert.That(slots.TryInsert(engine, controller.FuelSlot, fuel, user), Is.True);
                ranks.Levels[(int) ProfessionalSkill.Engineering] = 0;
                Assert.That(slots.CanEject(engine, user, controller.FuelSlot), Is.False,
                    "The alternative fuel-ejection verb must not bypass skills.");
                ranks.Levels[(int) ProfessionalSkill.Engineering] = 1;
                Assert.That(slots.CanEject(engine, user, controller.FuelSlot), Is.True);

                foreach (var (id, required) in new[] { ("SkillBasicSurgeryTestStep", 2), ("SkillAdvancedSurgeryTestStep", 3) })
                {
                    var step = em.SpawnEntity(id, map.GridCoords);
                    for (var level = 0; level <= 4; level++)
                    {
                        ranks.Levels[(int) ProfessionalSkill.Medicine] = level;
                        var attempt = new Content.Shared._Starlight.Medical.Surgery.Events.SurgeryCanPerformStepEvent(
                            user, other, [], Content.Shared.Inventory.SlotFlags.All);
                        em.EventBus.RaiseLocalEvent(step, ref attempt);
                        Assert.That(attempt.Invalid == Content.Shared._Starlight.Medical.Surgery.StepInvalidReason.Skill,
                            Is.EqualTo(level < required), $"{id}: medicine {level}");
                    }
                    em.DeleteEntity(step);
                }

                var clarke = em.SpawnEntity("MechClarke", map.GridCoords);
                var mechs = em.System<SharedMechSystem>();
                ranks.Levels[(int) ProfessionalSkill.Salvage] = 2;
                Assert.That(mechs.CanInsert(clarke, user), Is.False);
                Assert.That(mechs.TryInsert(clarke, user), Is.False);
                ranks.Levels[(int) ProfessionalSkill.Salvage] = 3;
                Assert.That(mechs.CanInsert(clarke, user), Is.True);
                em.DeleteEntity(clarke);

                var tray = em.SpawnEntity("HydroponicsTrayEmpty", map.GridCoords);
                var seed = em.SpawnEntity("WheatSeeds", map.GridCoords);
                ranks.Levels[(int) ProfessionalSkill.Botany] = 0;
                var interaction = new InteractUsingEvent(user, seed, tray, map.GridCoords);
                em.EventBus.RaiseLocalEvent(tray, interaction);
                Assert.That(em.GetComponent<PlantHolderComponent>(tray).Seed, Is.Null);
                ranks.Levels[(int) ProfessionalSkill.Botany] = 1;
                interaction = new InteractUsingEvent(user, seed, tray, map.GridCoords);
                em.EventBus.RaiseLocalEvent(tray, interaction);
                Assert.That(em.GetComponent<PlantHolderComponent>(tray).Seed, Is.Not.Null);

                var oven = em.SpawnEntity("KitchenElectricRange", map.GridCoords);
                em.GetComponent<ApcPowerReceiverComponent>(oven).Powered = true;
                var microwave = em.GetComponent<MicrowaveComponent>(oven);
                microwave.CurrentCookTimerTime = 5;
                var dough = em.SpawnEntity("FoodDoughSlice", map.GridCoords);
                Assert.That(em.System<SharedContainerSystem>().Insert(dough, microwave.Storage), Is.True);
                ranks.Levels[(int) ProfessionalSkill.Cooking] = 0;
                em.System<MicrowaveSystem>().Wzhzhzh(oven, microwave, user);
                Assert.That(em.HasComponent<ActiveMicrowaveComponent>(oven), Is.False);
                ranks.Levels[(int) ProfessionalSkill.Cooking] = 1;
                em.System<MicrowaveSystem>().Wzhzhzh(oven, microwave, user);
                Assert.That(em.HasComponent<ActiveMicrowaveComponent>(oven), Is.True,
                    "A novice cook must be able to start the electric oven with a simple recipe.");
                em.DeleteEntity(oven);
                Array.Clear(ranks.Levels);
            });

            foreach (var self in new[] { true, false })
            {
                await pair.Server.WaitAssertion(() =>
                {
                    pen = em.SpawnEntity("EmergencyMedipen", map.GridCoords);
                    Assert.That(em.System<HandsSystem>().TryPickupAnyHand(user, pen), Is.True);
                    Assert.That(em.System<HypospraySystem>().TryDoInject(
                        (pen, em.GetComponent<HyposprayComponent>(pen)), self ? user : other, user), Is.True);
                    Assert.That(PenVolume(), Is.GreaterThan(0), "The injection must not happen immediately.");
                });
                await pair.Server.WaitRunTicks(60);
                await pair.Server.WaitAssertion(() => Assert.That(PenVolume(), Is.GreaterThan(0)));
                await pair.Server.WaitRunTicks(150);
                await pair.Server.WaitAssertion(() =>
                {
                    Assert.That(PenVolume(), Is.Zero, "The novice injection completes after 3 seconds.");
                    em.DeleteEntity(pen);
                });
            }
            await pair.Server.WaitAssertion(() =>
            {
                em.GetComponent<ProfessionalSkillsComponent>(user).Levels[(int) ProfessionalSkill.Medicine] = 1;
                pen = em.SpawnEntity("EmergencyMedipen", map.GridCoords);
                Assert.That(em.System<HypospraySystem>().TryDoInject(
                    (pen, em.GetComponent<HyposprayComponent>(pen)), user, user), Is.True);
                Assert.That(PenVolume(), Is.Zero, "A trained user retains immediate self-injection.");
            });

            float PenVolume()
            {
                var injector = em.GetComponent<HyposprayComponent>(pen);
                Assert.That(em.System<SharedSolutionContainerSystem>().TryGetSolution(pen, injector.SolutionName,
                    out _, out var solution), Is.True);
                return solution!.Volume.Float();
            }
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}

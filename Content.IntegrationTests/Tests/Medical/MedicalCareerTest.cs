using System.Linq;
using Content.Server._radiant.Careers;
using Content.Server._radiant.Medical;
using Content.Server._radiant.Medical.Genetics;
using Content.Server._radiant.Medical.Virology;
using Content.Server.Power.Components;
using Content.Server.Preferences.Managers;
using Content.Shared._radiant.Careers;
using Content.Shared._radiant.Skills;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Damage.Prototypes;
using Content.Shared.EntityEffects;
using Content.Shared.EntityEffects.Effects;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs.Systems;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class MedicalCareerTest
{
    [Test]
    public async Task HealingChemistryLaboratoriesAndWearableLimits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.ResolveDependency<IEntityManager>();
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                var prefs = pair.Server.ResolveDependency<IServerPreferencesManager>().GetPreferences(session.UserId);
                var profile = (HumanoidCharacterProfile) prefs.SelectedCharacter;
                var original = profile.Career;
                profile.Career = new CareerData { CharacterId = original.CharacterId, Profession = "Medical" };
                try
                {
                    var doctor = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                    var patient = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                    var actor = em.AddComponent<CareerActorComponent>(doctor);
                    actor.User = session.UserId;
                    actor.Slot = prefs.SelectedCharacterIndex;
                    actor.CharacterId = profile.Career.CharacterId;
                    actor.Job = "MedicalDoctor";
                    var careers = em.System<CareerSystem>();
                    var medical = em.System<CareerMedicalSystem>();
                    var damage = em.System<DamageableSystem>();
                    var protos = pair.Server.ResolveDependency<IPrototypeManager>();
                    var blunt = protos.Index<DamageTypePrototype>("Blunt");
                    Assert.That(careers.IsWorking(doctor), Is.True);
                    Assert.That(careers.IsJobAllowed(session, "MedicalDoctor"), Is.True);
                    Assert.That(careers.IsJobAllowed(session, "SalvageSpecialist"), Is.False);
                    var miningBase = em.SpawnEntity(null, map.GridCoords);
                    em.AddComponent<CareerBaseStationComponent>(miningBase).Professions.Add("Mining");
                    var medicalBase = em.SpawnEntity(null, map.GridCoords);
                    em.AddComponent<CareerBaseStationComponent>(medicalBase).Professions.Add("Medical");
                    var fleet = em.System<CareerFleetSystem>();
                    Assert.That(fleet.CanJoin(session, miningBase, "MedicalDoctor", out _), Is.False);
                    Assert.That(fleet.CanJoin(session, medicalBase, "MedicalDoctor", out _), Is.True);
                    damage.TryChangeDamage(patient, new DamageSpecifier(blunt, 50), true);
                    damage.TryChangeDamage(patient, new DamageSpecifier(blunt, -20), true, origin: doctor);
                    Assert.That(profile.Career.Progress["healing"], Is.EqualTo(20));
                    damage.TryChangeDamage(doctor, new DamageSpecifier(blunt, 10), true);
                    damage.TryChangeDamage(doctor, new DamageSpecifier(blunt, -10), true, origin: doctor);
                    Assert.That(profile.Career.Progress["healing"], Is.EqualTo(20), "Self-treatment does not count");
                    var dose = new Solution();
                    dose.AddReagent("Bicaridine", 10);
                    var medication = new CareerMedicineAdministeredEvent(doctor, patient, dose);
                    em.EventBus.RaiseEvent(EventSource.Local, ref medication);
                    var reagent = protos.Index<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Bicaridine");
                    var chemicalEffect = new HealthChange { Damage = new DamageSpecifier(blunt, -5) };
                    chemicalEffect.Effect(new EntityEffectReagentArgs(patient, em, null, dose, 1, reagent, null, 1));
                    Assert.That(profile.Career.Progress["healing"], Is.EqualTo(25));
                    var groupEffect = new EvenHealthChange();
                    groupEffect.Damage.Add("Brute", -5);
                    groupEffect.Effect(new EntityEffectReagentArgs(patient, em, null, dose, 1, reagent, null, 1));
                    Assert.That(profile.Career.Progress["healing"], Is.EqualTo(30), "Group-based medicines count on stage one");
                    var corpse = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                    damage.TryChangeDamage(corpse, new DamageSpecifier(blunt, 300), true);
                    em.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
                    damage.TryChangeDamage(corpse, new DamageSpecifier(blunt, -5), true, origin: doctor);
                    Assert.That(profile.Career.Progress["healing"], Is.EqualTo(35), "Treating a dead patient's injuries counts");
                    em.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
                    medication = new CareerMedicineAdministeredEvent(doctor, corpse, dose);
                    em.EventBus.RaiseEvent(EventSource.Local, ref medication);
                    groupEffect.Effect(new EntityEffectReagentArgs(corpse, em, null, dose, 1, reagent, null, 1));
                    Assert.That(profile.Career.Progress["healing"], Is.EqualTo(40), "Chemical treatment of a corpse counts");
                    var oldStage = medical.Capture(doctor);
                    medical.RecordTask(doctor, CareerObjectiveType.Healing, amount: 960);
                    Assert.That(profile.Career.Level, Is.EqualTo(1));
                    Assert.That(profile.Career.PendingRewards, Is.EqualTo(new[] { 1 }));
                    medical.Complete(oldStage, CareerObjectiveType.Synthesis, "Bicaridine");
                    Assert.That(profile.Career.Progress, Is.Empty, "An old operation cannot count in a new stage");

                    var beaker = em.SpawnEntity("Beaker", map.GridCoords);
                    var solutions = em.System<SharedSolutionContainerSystem>();
                    Assert.That(solutions.TryGetSolution(beaker, "beaker", out var solution), Is.True);
                    var operation = new CareerChemicalOperationEvent(doctor, beaker);
                    em.EventBus.RaiseEvent(EventSource.Local, ref operation);
                    var mixture = new Solution();
                    mixture.AddReagent("Inaprovaline", 10);
                    mixture.AddReagent("Carbon", 10);
                    Assert.That(solutions.TryAddSolution(solution!.Value, mixture), Is.True);
                    Assert.That(profile.Career.Progress["bicaridine"], Is.EqualTo(20));
                    var readyMedicine = new Solution();
                    readyMedicine.AddReagent("Bicaridine", 10);
                    Assert.That(solutions.TryAddSolution(solution.Value, readyMedicine), Is.True);
                    Assert.That(profile.Career.Progress["bicaridine"], Is.EqualTo(20), "Pouring finished medicine is not synthesis");

                    profile.Career.Level = 2;
                    profile.Career.Progress.Clear();
                    var administered = new CareerMedicineAdministeredEvent(doctor, patient, readyMedicine);
                    em.EventBus.RaiseEvent(EventSource.Local, ref administered);
                    var effect = new HealthChange { Damage = new DamageSpecifier(blunt, -5) };
                    effect.Effect(new EntityEffectReagentArgs(patient, em, null, readyMedicine, 1,
                        protos.Index<Content.Shared.Chemistry.Reagent.ReagentPrototype>("Bicaridine"), null, 1));
                    Assert.That(profile.Career.Progress["chemical-blunt"], Is.EqualTo(5));
                    damage.TryChangeDamage(patient, new DamageSpecifier(blunt, -5), true, origin: doctor);
                    Assert.That(profile.Career.Progress["chemical-blunt"], Is.EqualTo(5), "Topicals are not chemical treatment");
                    groupEffect.Effect(new EntityEffectReagentArgs(patient, em, null, readyMedicine, 1, reagent, null, 1));
                    Assert.That(profile.Career.Progress["chemical-blunt"], Is.EqualTo(10), "Group healing also counts on the chemical stage");
                    em.System<MobStateSystem>().ChangeMobState(corpse, MobState.Dead);
                    medication = new CareerMedicineAdministeredEvent(doctor, corpse, dose);
                    em.EventBus.RaiseEvent(EventSource.Local, ref medication);
                    chemicalEffect.Effect(new EntityEffectReagentArgs(corpse, em, null, dose, 1, reagent, null, 1));
                    Assert.That(profile.Career.Progress["chemical-blunt"], Is.EqualTo(15));

                    var synth = em.SpawnEntity("RestorativeSynthesizer", map.GridCoords);
                    var recovery = em.System<RestorativeSynthesizerSystem>();
                    Assert.That(recovery.TryHealWearer(synth), Is.False, "A floor item cannot heal");
                    var skills = em.EnsureComponent<ProfessionalSkillsComponent>(doctor);
                    skills.Levels[(int) ProfessionalSkill.Medicine] = 1;
                    var inventory = em.System<InventorySystem>();
                    Assert.That(inventory.TryEquip(doctor, synth, "belt", force: true), Is.True);
                    damage.TryChangeDamage(doctor, new DamageSpecifier(blunt, 20), true);
                    var health = em.GetComponent<DamageableComponent>(doctor);
                    var before = health.TotalDamage;
                    Assert.That(recovery.TryHealWearer(synth), Is.True);
                    Assert.That(health.TotalDamage, Is.EqualTo(before - 0.25));
                    damage.TryChangeDamage(doctor, new DamageSpecifier(blunt, 100 - health.TotalDamage), true);
                    Assert.That(recovery.TryHealWearer(synth), Is.False, "Exactly 100 damage is not minor injury");
                    damage.TryChangeDamage(doctor, new DamageSpecifier(blunt, -90), true);
                    skills.Levels[(int) ProfessionalSkill.Medicine] = 0;
                    Assert.That(recovery.TryHealWearer(synth), Is.False, "Basic medical training is required");
                    skills.Levels[(int) ProfessionalSkill.Medicine] = 4;

                    profile.Career.Level = 4;
                    profile.Career.Progress.Clear();
                    var console = em.SpawnEntity("GeneticResearchConsole", map.GridCoords);
                    em.GetComponent<ApcPowerReceiverComponent>(console).Powered = true;
                    var genetics = em.System<GeneticBodySystem>();
                    var cartridge = em.SpawnEntity("GeneticSampleCartridge", map.GridCoords);
                    Assert.That(genetics.Collect(cartridge, patient), Is.True);
                    var research = em.GetComponent<GeneticBodyGrowerComponent>(console);
                    Assert.That(em.System<SharedContainerSystem>().Insert(cartridge, research.Sample), Is.True);
                    Assert.That(genetics.TryStart(console), Is.True);
                    genetics.Advance(console, research.AnalysisSeconds);
                    for (var i = 0; i < 3; i++)
                    {
                        Assert.That(genetics.TryResearch(console), Is.True);
                        genetics.Advance(console, research.ResearchSeconds);
                    }
                    var answer = genetics.BuildUiState(console).SequencePattern;
                    Assert.That(answer.Contains('?'), Is.False);
                    Assert.That(genetics.TrySequence(console, answer), Is.True);
                    research.CareerOperator = medical.Capture(doctor);
                    genetics.Advance(console, 10);
                    Assert.That(profile.Career.Progress["dna"], Is.EqualTo(1));
                    Assert.That(genetics.TrySequence(console, answer), Is.False, "Confirmed DNA cannot be counted twice");

                    profile.Career.Level = 5;
                    profile.Career.Progress.Clear();
                    var diseases = em.System<VirologySystem>();
                    Assert.That(diseases.Expose(patient, "StationFever", 100), Is.True);
                    var swab = em.SpawnEntity("DiseaseSwab", map.GridCoords);
                    var laboratory = em.System<VirologyLabSystem>();
                    Assert.That(laboratory.Collect(swab, patient), Is.True);
                    var diagnoser = em.SpawnEntity("DiseaseDiagnoser", map.GridCoords);
                    em.GetComponent<ApcPowerReceiverComponent>(diagnoser).Powered = true;
                    Assert.That(laboratory.Insert(diagnoser, swab), Is.True);
                    Assert.That(laboratory.Start(diagnoser, Content.Shared._radiant.Medical.Virology.VirologyCommand.Analyze), Is.True);
                    var analysis = em.GetComponent<VirologyMachineComponent>(diagnoser);
                    analysis.CareerOperator = medical.Capture(doctor);
                    laboratory.Advance(diagnoser, analysis.AnalysisSeconds);
                    Assert.That(profile.Career.Progress["diagnosis"], Is.EqualTo(1));
                    Assert.That(laboratory.Start(diagnoser, Content.Shared._radiant.Medical.Virology.VirologyCommand.Analyze), Is.False);
                }
                finally
                {
                    profile.Career = original;
                }
            });
        }
        finally
        {
            await pair.Server.WaitPost(() => pair.Server.EntMan.DeleteEntity(map.MapUid));
            await pair.CleanReturnAsync();
        }
    }
}

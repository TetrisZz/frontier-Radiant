using System.Linq;
using Content.Server.Power.Components;
using Content.Server._radiant.Medical.Genetics;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Forensics.Components;
using Content.Shared.Humanoid;
using Content.Shared.Materials;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared._radiant.Medical.Genetics;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class GeneticModificationTest
{
    [Test]
    public async Task ResearchSynthesisPersonalizationAdaptationAndRemoval()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var em = server.ResolveDependency<IEntityManager>();
        await server.WaitAssertion(() =>
        {
            var maps = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>();
            var map = maps.CreateMap();
            var grid = maps.CreateGridEntity(map);
            em.System<SharedMapSystem>().SetTile(grid.Owner, grid.Comp, Vector2i.Zero, new Tile(1));
            var pos = new EntityCoordinates(grid.Owner, .5f, .5f);
            var patient = em.SpawnEntity("MobHuman", pos);
            var stranger = em.SpawnEntity("MobHuman", pos);
            em.System<SharedHumanoidAppearanceSystem>().SetSex(patient, Sex.Female);
            var pod = em.SpawnEntity("GeneticBodyGrower", pos);
            Assert.That(em.GetComponent<TransformComponent>(pod).Anchored, Is.True);
            em.GetComponent<ApcPowerReceiverComponent>(pod).Powered = true;
            var machine = em.GetComponent<GeneticBodyGrowerComponent>(pod);
            var sample = em.SpawnEntity("GeneticSampleCartridge", pos);
            var genetics = em.System<GeneticBodySystem>();
            var modifications = em.System<GeneticModificationSystem>();
            var materials = em.System<SharedMaterialStorageSystem>();
            Assert.That(genetics.Collect(sample, patient), Is.True);
            Assert.That(genetics.TrySetResearchSequence(sample, "RadiantHematopoiesis", "ATGCAT"), Is.True);
            Assert.That(genetics.TrySetResearchSequence(sample, "RadiantHematopoiesis", "invalid"), Is.False);
            Assert.That(genetics.TryGetResearchSequence(sample, "RadiantHematopoiesis", out var testSequence), Is.True);
            Assert.That(testSequence, Is.EqualTo("ATGCAT"));
            var data = em.GetComponent<GeneticSampleComponent>(sample);
            var snapshot = data.ProfileSnapshot;
            Assert.That(em.System<SharedContainerSystem>().Insert(sample, machine.Sample), Is.True);
            Assert.That(genetics.TryResearch(pod), Is.False);
            Assert.That(genetics.TryStart(pod), Is.True);
            Assert.That(genetics.TrySetResearchSequence(sample, "RadiantHematopoiesis", "AAAAAA"), Is.False);
            genetics.Advance(pod, machine.AnalysisSeconds);
            Assert.That(genetics.SelectProduct(pod, GeneticProduct.Hematopoiesis), Is.False);
            Assert.That(genetics.BuildUiState(pod).CanResearch, Is.True);
            Assert.That(genetics.TryResearch(pod), Is.True);
            Assert.That(genetics.TryResearch(pod), Is.False);
            genetics.Advance(pod, machine.ResearchSeconds);
            Assert.That(data.Researched, Is.True);
            Assert.That(genetics.BuildUiState(pod).Products, Does.Not.Contain(GeneticProduct.Hematopoiesis),
                "Studying a sample reveals clues, not an automatic therapy unlock.");
            var firstPattern = genetics.BuildUiState(pod).SequencePattern;
            Assert.That(firstPattern.Count(c => c != '?'), Is.EqualTo(2));
            Assert.That(genetics.TrySpectrum(pod), Is.True);
            genetics.Advance(pod, 30);
            Assert.That(genetics.BuildUiState(pod).CanSpectrum, Is.False);
            Assert.That(genetics.BuildUiState(pod).Composition, Does.Contain("2"));
            Assert.That(genetics.BuildUiState(pod).Composition, Does.Contain("1"));
            Assert.That(genetics.TrySequence(pod, "DROP TABLE"), Is.False);
            var disk = em.SpawnEntity("GeneticResearchDisk", pos);
            Assert.That(genetics.TryInsertResearchDisk(pod, disk), Is.True);
            foreach (var gene in new[] { "RadiantHematopoiesis", "RadiantCoagulation", "RadiantRegeneration" })
            {
                machine.SelectedGene = gene;
                while (genetics.TryResearch(pod))
                    genetics.Advance(pod, machine.ResearchSeconds);
                var pattern = genetics.BuildUiState(pod).SequencePattern;
                Assert.That(pattern, Does.Not.Contain("?"));
                var wrong = (pattern[0] == 'A' ? "T" : "A") + pattern[1..];
                Assert.That(genetics.TrySequence(pod, wrong), Is.True);
                Assert.That(genetics.TrySequence(pod, pattern), Is.False, "A pending check cannot be replaced.");
                em.GetComponent<ApcPowerReceiverComponent>(pod).Powered = false;
                genetics.Advance(pod, 5);
                Assert.That(machine.Remaining, Is.EqualTo(10));
                em.GetComponent<ApcPowerReceiverComponent>(pod).Powered = true;
                genetics.Advance(pod, 10);
                Assert.That(machine.Discoveries, Does.Not.Contain(gene));
                Assert.That(genetics.TrySequence(pod, pattern), Is.True);
                genetics.Advance(pod, 10);
                Assert.That(machine.Discoveries, Does.Contain(gene));
            }
            var secondPod = em.SpawnEntity("GeneticBodyGrower", pos);
            Assert.That(em.GetComponent<TransformComponent>(secondPod).Anchored, Is.True);
            em.GetComponent<ApcPowerReceiverComponent>(secondPod).Powered = true;
            Assert.That(em.System<SharedContainerSystem>().Remove(disk, machine.ResearchDisk), Is.True);
            Assert.That(genetics.TryInsertResearchDisk(secondPod, disk), Is.True);
            Assert.That(em.GetComponent<GeneticBodyGrowerComponent>(secondPod).Discoveries.Count, Is.EqualTo(3));
            Assert.That(genetics.BuildUiState(secondPod).CanEjectDisk, Is.True, "Disk must be ejectable without a sample.");
            var otherSample = em.SpawnEntity("GeneticSampleCartridge", pos);
            Assert.That(genetics.Collect(otherSample, stranger), Is.True);
            var secondMachine = em.GetComponent<GeneticBodyGrowerComponent>(secondPod);
            Assert.That(em.System<SharedContainerSystem>().Insert(otherSample, secondMachine.Sample), Is.True);
            Assert.That(genetics.TryStart(secondPod), Is.True);
            genetics.Advance(secondPod, secondMachine.AnalysisSeconds);
            Assert.That(genetics.BuildUiState(secondPod).SequencePattern, Is.EqualTo("??????"));
            Assert.That(genetics.SelectProduct(secondPod, GeneticProduct.Hematopoiesis), Is.False,
                "General knowledge on a disk does not decode another patient's DNA.");
            Assert.That(genetics.TryGetResearchSequence(otherSample, "RadiantHematopoiesis", out var otherSequence), Is.True);
            Assert.That(otherSequence, Is.Not.EqualTo(testSequence));
            Assert.That(em.System<SharedContainerSystem>().Remove(otherSample, secondMachine.Sample), Is.True);
            var copy = em.SpawnEntity("GeneticSampleCartridge", pos);
            Assert.That(genetics.Collect(copy, patient), Is.True);
            Assert.That(em.System<SharedContainerSystem>().Insert(copy, secondMachine.Sample), Is.True);
            Assert.That(genetics.TryStart(secondPod), Is.True);
            genetics.Advance(secondPod, secondMachine.AnalysisSeconds);
            Assert.That(genetics.BuildUiState(secondPod).SequencePattern, Is.EqualTo(testSequence),
                "Another cartridge and another machine must retain this donor's research.");
            Assert.That(genetics.SelectProduct(secondPod, GeneticProduct.Hematopoiesis), Is.True);
            Assert.That(genetics.BuildUiState(pod).Products, Does.Contain(GeneticProduct.Hematopoiesis));
            Assert.That(genetics.SelectProduct(pod, GeneticProduct.Hematopoiesis), Is.True);
            materials.TryChangeMaterialAmount(pod, "Biomass", machine.TherapyBiomassCost * 2);
            Assert.That(genetics.TryStart(pod), Is.True);
            Assert.That(machine.Remaining, Is.EqualTo(machine.TherapySeconds));
            genetics.Advance(pod, machine.TherapySeconds);
            var injector = genetics.TryRelease(pod)!.Value;
            Assert.That(em.GetComponent<MetaDataComponent>(injector).EntityPrototype!.ID, Is.EqualTo("GeneticHematopoiesisInjector"));
            Assert.That(em.GetComponent<GeneticTherapyComponent>(injector).Dna,
                Is.EqualTo(em.GetComponent<DnaComponent>(patient).DNA));
            Assert.That(modifications.Apply(injector, stranger), Is.False);
            Assert.That(em.GetComponent<GeneticTherapyComponent>(injector).Used, Is.False);

            var profile = em.EnsureComponent<GeneticProfileComponent>(patient);
            profile.Capacity = 20;
            Assert.That(modifications.Failure(injector, patient), Is.EqualTo("genetics-therapy-overload"));
            Assert.That(modifications.Apply(injector, patient), Is.False);
            profile.Capacity = 100;
            Assert.That(modifications.Apply(injector, patient), Is.True);
            Assert.That(modifications.Load(profile), Is.EqualTo(40));
            var clotting = em.SpawnEntity("GeneticCoagulationInjector", pos);
            Assert.That(modifications.Failure(clotting, patient), Is.EqualTo("genetics-therapy-conflict"));
            Assert.That(modifications.Apply(injector, patient), Is.False);
            var duplicate = em.SpawnEntity("GeneticHematopoiesisInjector", pos);
            Assert.That(modifications.Failure(duplicate, patient), Is.EqualTo("genetics-therapy-already-present"));
            Assert.That(em.GetComponent<GeneticTherapyComponent>(duplicate).Used, Is.False);
            Assert.That(data.ProfileSnapshot, Is.EqualTo(snapshot), "Old samples must not become a live patient monitor.");

            var blood = em.GetComponent<BloodstreamComponent>(patient);
            var solutions = em.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(patient, blood.BloodSolutionName, out var bloodEntity, out var liquid), Is.True);
            solutions.SplitSolution(bloodEntity!.Value, FixedPoint2.New(20));
            var beforeBlood = liquid.Volume.Float();
            var hunger = em.GetComponent<HungerComponent>(patient);
            var nutrition = em.System<HungerSystem>();
            nutrition.SetHunger(patient, 200);
            var beforeNutrition = nutrition.GetHunger(hunger);
            modifications.Advance(patient, 120);
            Assert.That(liquid.Volume.Float(), Is.EqualTo(beforeBlood).Within(.001), "Adaptation has no immediate effect.");
            modifications.Advance(patient, 60);
            Assert.That(liquid.Volume.Float(), Is.EqualTo(beforeBlood + 3).Within(.01));
            Assert.That(nutrition.GetHunger(hunger), Is.EqualTo(beforeNutrition - 1.5f).Within(.01));
            nutrition.SetHunger(patient, 0);
            beforeBlood = liquid.Volume.Float();
            modifications.Advance(patient, 60);
            Assert.That(liquid.Volume.Float(), Is.EqualTo(beforeBlood).Within(.01), "Starvation disables the enhancement.");
            Assert.That(genetics.SelectProduct(pod, GeneticProduct.HematopoiesisRemoval), Is.True);
            Assert.That(genetics.TryStart(pod), Is.True);
            genetics.Advance(pod, machine.TherapySeconds);
            var suppressor = genetics.TryRelease(pod)!.Value;
            Assert.That(modifications.Apply(suppressor, patient), Is.True);
            Assert.That(modifications.Load(profile), Is.Zero);
            nutrition.SetHunger(patient, 200);
            modifications.Advance(patient, 60);
            Assert.That(liquid.Volume.Float(), Is.EqualTo(beforeBlood).Within(.01));
            Assert.That(materials.GetMaterialAmount(pod, "Biomass"), Is.Zero);

            // The same remover can also stop adaptation; ineffective doses remain unused.
            var unused = em.SpawnEntity("GeneticHematopoiesisRemover", pos);
            Assert.That(modifications.Apply(unused, patient), Is.False);
            Assert.That(em.GetComponent<GeneticTherapyComponent>(unused).Used, Is.False);
            Assert.That(modifications.Apply(duplicate, patient), Is.True);
            Assert.That(modifications.Apply(unused, patient), Is.True);
            Assert.That(profile.Modifications, Is.Empty);
            Assert.That(modifications.Apply(clotting, patient), Is.True);
            em.System<Content.Shared.Body.Systems.SharedBloodstreamSystem>().TryModifyBleedAmount((patient, blood), 5 - blood.BleedAmount);
            modifications.Advance(patient, 120);
            Assert.That(blood.BleedAmount, Is.EqualTo(5));
            modifications.Advance(patient, 60);
            Assert.That(blood.BleedAmount, Is.EqualTo(3.8f).Within(.01));
            var clottingRemover = em.SpawnEntity("GeneticCoagulationRemover", pos);
            Assert.That(modifications.Apply(clottingRemover, patient), Is.True);
            modifications.Advance(patient, 60);
            Assert.That(blood.BleedAmount, Is.EqualTo(3.8f).Within(.01));
            var repair = em.SpawnEntity("GeneticRegenerationInjector", pos);
            Assert.That(modifications.Apply(repair, patient), Is.True);
            var damage = new Content.Shared.Damage.DamageSpecifier();
            damage.DamageDict["Blunt"] = FixedPoint2.New(10);
            em.System<Content.Shared.Damage.DamageableSystem>().TryChangeDamage(patient, damage, ignoreResistances: true);
            var damaged = em.GetComponent<Content.Shared.Damage.DamageableComponent>(patient);
            modifications.Advance(patient, 180);
            var beforeRepair = damaged.Damage.DamageDict["Blunt"].Float();
            modifications.Advance(patient, 60);
            Assert.That(damaged.Damage.DamageDict["Blunt"].Float(), Is.EqualTo(beforeRepair - .9f).Within(.01));
            nutrition.SetHunger(patient, 0);
            beforeRepair = damaged.Damage.DamageDict["Blunt"].Float();
            modifications.Advance(patient, 60);
            Assert.That(damaged.Damage.DamageDict["Blunt"].Float(), Is.EqualTo(beforeRepair).Within(.01));
            Assert.That(genetics.TrySetResearchSequence(sample, "RadiantHematopoiesis", "AAAAAA"), Is.True);
            Assert.That(genetics.SelectProduct(secondPod, GeneticProduct.Hematopoiesis), Is.False);
            Assert.That(em.GetComponent<GeneticResearchDiskComponent>(disk).Discoveries, Does.Contain("RadiantHematopoiesis"),
                "Changing one donor's code does not erase general gene knowledge.");
        });
    }
}

using Content.Server.Power.Components;
using Content.Server._radiant.Medical.Genetics;
using Content.Shared.Humanoid;
using Content.Shared.DeviceLinking;
using Content.Shared.Audio;
using Content.Shared.Materials;
using Content.Shared._radiant.Medical.Genetics;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class GeneticConsoleTest
{
    [Test]
    public async Task IndependentResearchAndSnapshotProduction()
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
            var console = em.SpawnEntity("GeneticResearchConsole", pos);
            var pod = em.SpawnEntity("GeneticBodyGrower", pos);
            Assert.That(em.GetComponent<TransformComponent>(console).Anchored, Is.True);
            Assert.That(em.GetComponent<TransformComponent>(pod).Anchored, Is.True);
            var consolePower = em.GetComponent<ApcPowerReceiverComponent>(console);
            var podPower = em.GetComponent<ApcPowerReceiverComponent>(pod);
            consolePower.Powered = true;
            podPower.Powered = true;
            var genetics = em.System<GeneticBodySystem>();
            var containers = em.System<SharedContainerSystem>();
            var materials = em.System<SharedMaterialStorageSystem>();
            var research = em.GetComponent<GeneticBodyGrowerComponent>(console);
            var machine = em.GetComponent<GeneticBodyGrowerComponent>(pod);
            var patient = em.SpawnEntity("MobHuman", pos);
            em.System<SharedHumanoidAppearanceSystem>().SetSex(patient, Sex.Female);
            var cartridge = em.SpawnEntity("GeneticSampleCartridge", pos);
            Assert.That(genetics.Collect(cartridge, patient), Is.True);
            Assert.That(containers.Insert(cartridge, research.Sample), Is.True);
            Assert.That(genetics.BuildUiState(console).CanGrow, Is.False);
            Assert.That(genetics.TryStart(console), Is.True, "Console analysis works without any capsule.");
            genetics.Advance(console, 0);
            Assert.That(em.GetComponent<AmbientSoundComponent>(console).Enabled, Is.True);
            consolePower.Powered = false;
            genetics.Advance(console, 0);
            Assert.That(em.GetComponent<AmbientSoundComponent>(console).Enabled, Is.False);
            consolePower.Powered = true;
            genetics.Advance(console, 0);
            Assert.That(em.GetComponent<AmbientSoundComponent>(console).Enabled, Is.True);
            genetics.Advance(console, research.AnalysisSeconds);
            Assert.That(em.GetComponent<AmbientSoundComponent>(console).Enabled, Is.False,
                "Finished analysis must stop the scanning loop.");
            Assert.That(genetics.TryResearch(console), Is.True);
            genetics.Advance(console, research.ResearchSeconds);
            Assert.That(genetics.BuildUiState(console).SequencePattern, Is.Not.EqualTo("??????"));
            Assert.That(genetics.TryStart(console), Is.False, "A console must not grow a local culture.");
            Assert.That(genetics.TryOrder(console), Is.False);
            Assert.That(genetics.TryLinkConsole(console, console), Is.False);
            Assert.That(genetics.TryLinkConsole(console, pod), Is.True);
            var links = em.System<SharedDeviceLinkSystem>();
            Assert.That(links.GetLinks(console, pod).Count, Is.EqualTo(1),
                "The capsule must use the standard device linking graph.");
            links.RemoveSinkFromSource(console, pod);
            Assert.That(links.GetLinks(console, pod), Is.Empty);
            Assert.That(genetics.TryOrder(console), Is.False, "Unlinking must invalidate the production connection.");
            links.LinkDefaults(null, console, pod);
            Assert.That(links.GetLinks(console, pod).Count, Is.EqualTo(1));
            var otherPod = em.SpawnEntity("GeneticBodyGrower", pos);
            Assert.That(em.GetComponent<TransformComponent>(otherPod).Anchored, Is.True);
            links.LinkDefaults(null, console, otherPod);
            Assert.That(links.GetLinks(console, otherPod), Is.Empty,
                "A console must not ambiguously control two capsules.");
            Assert.That(genetics.SelectProduct(console, GeneticProduct.Heart), Is.True);
            materials.TryChangeMaterialAmount(pod, "Biomass", machine.OrganBiomassCost + machine.TherapyBiomassCost);
            podPower.Powered = false;
            Assert.That(genetics.BuildUiState(console).CanGrow, Is.False);
            Assert.That(genetics.TryOrder(console), Is.False);
            podPower.Powered = true;
            Assert.That(genetics.BuildUiState(console).CanGrow, Is.True);
            Assert.That(genetics.TryOrder(console), Is.True);
            genetics.Advance(pod, 0);
            Assert.That(em.GetComponent<AmbientSoundComponent>(pod).Enabled, Is.True);
            Assert.That(genetics.TryOrder(console), Is.False, "An occupied capsule cannot be charged a second time.");
            Assert.That(materials.GetMaterialAmount(pod, "Biomass"), Is.EqualTo(machine.TherapyBiomassCost));
            Assert.That(research.Sample.ContainedEntity, Is.EqualTo(cartridge));
            Assert.That(machine.Sample.ContainedEntity, Is.Null);
            Assert.That(machine.ProductionSample, Is.Not.Null);
            // Removing/changing the original sample must not alter or abort an accepted order.
            Assert.That(containers.Remove(cartridge, research.Sample), Is.True);
            var original = em.GetComponent<GeneticSampleComponent>(cartridge);
            var dna = original.Dna;
            original.Dna = "changed-after-order";
            Assert.That(machine.ProductionSample!.Dna, Is.EqualTo(dna));
            consolePower.Powered = false;
            genetics.Advance(pod, machine.OrganGrowthSeconds);
            Assert.That(machine.Stage, Is.EqualTo(GeneticGrowthStage.Ready));
            Assert.That(genetics.TryRelease(console), Is.Null);
            var organ = genetics.TryRelease(pod);
            Assert.That(organ, Is.Not.Null);
            Assert.That(em.GetComponent<MetaDataComponent>(organ!.Value).EntityPrototype!.ID, Is.EqualTo("OrganHumanHeart"));
            Assert.That(machine.ProductionSample, Is.Null);
            Assert.That(genetics.TryRelease(pod), Is.Null);
            original.Dna = dna;
            consolePower.Powered = true;
            Assert.That(containers.Insert(cartridge, research.Sample), Is.True);
            // A discovered treatment is personalized at submission, not when collected.
            Assert.That(genetics.TryGetResearchSequence(cartridge, "RadiantHematopoiesis", out var code), Is.True);
            Assert.That(genetics.TrySequence(console, code), Is.True);
            genetics.Advance(console, 10);
            Assert.That(genetics.SelectProduct(console, GeneticProduct.Hematopoiesis), Is.True);
            Assert.That(genetics.TryOrder(console), Is.True);
            Assert.That(containers.Remove(cartridge, research.Sample), Is.True);
            original.Dna = "another-donor";
            genetics.Advance(pod, machine.TherapySeconds);
            var treatment = genetics.TryRelease(pod);
            Assert.That(treatment, Is.Not.Null);
            Assert.That(em.GetComponent<GeneticTherapyComponent>(treatment!.Value).Dna, Is.EqualTo(dna));
            Assert.That(materials.GetMaterialAmount(pod, "Biomass"), Is.Zero);
            Assert.That(machine.Discoveries, Is.Empty, "Production does not copy the console archive into a capsule.");
            em.DeleteEntity(pod);
            Assert.That(genetics.BuildUiState(console).CanGrow, Is.False);
            Assert.That(genetics.TryOrder(console), Is.False);
            Assert.That(research.Discoveries, Does.Contain("RadiantHematopoiesis"));
        });
    }
}

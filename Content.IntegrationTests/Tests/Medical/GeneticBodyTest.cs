using System.Linq;
using Content.Server.Body.Components;
using Content.Server.Power.Components;
using Content.Server._radiant.Medical.Genetics;
using Content.Server._radiant.Medical.Surgery;
using Content.Shared.Atmos.Rotting;
using Content.Shared.Body.Systems;
using Content.Shared.Forensics.Components;
using Content.Shared.Humanoid;
using Content.Shared.Materials;
using Content.Shared._radiant.Medical.Genetics;
using Content.Shared.Mind.Components;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class GeneticBodyTest
{
    [Test]
    public async Task BrainSampleGrowsOriginalAppearanceAndDna()
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
            var pos = new EntityCoordinates(grid.Owner, 0.5f, 0.5f);
            var donor = em.SpawnEntity("MobHuman", pos);
            var appearance = em.System<SharedHumanoidAppearanceSystem>();
            appearance.SetSex(donor, Sex.Female);
            appearance.SetHeight(donor, 1.1f);
            var donorDna = em.GetComponent<DnaComponent>(donor).DNA;
            var body = em.System<SharedBodySystem>();
            var brain = body.GetBodyChildren(donor).SelectMany(p => body.GetPartOrgans(p.Id))
                .First(o => em.HasComponent<BrainComponent>(o.Id)).Id;
            em.System<BrainRestorationSystem>().Capture(brain, donor);

            var sample = em.SpawnEntity("GeneticSampleCartridge", pos);
            var genetics = em.System<GeneticBodySystem>();
            Assert.That(genetics.Collect(sample, brain), Is.True);
            Assert.That(genetics.Collect(sample, brain), Is.False);
            var data = em.GetComponent<GeneticSampleComponent>(sample);
            Assert.That(data.BrainAppearance, Is.Not.Null);
            Assert.That(data.Dna, Is.EqualTo(donorDna));
            appearance.SetSex(donor, Sex.Male);
            // The saved brain sample must work even after the original body is gone.
            em.DeleteEntity(donor);

            var pod = em.SpawnEntity("GeneticBodyGrower", pos);
            Assert.That(em.GetComponent<TransformComponent>(pod).Anchored, Is.True);
            var machine = em.GetComponent<GeneticBodyGrowerComponent>(pod);
            em.GetComponent<ApcPowerReceiverComponent>(pod).Powered = true;
            Assert.That(em.System<SharedContainerSystem>().Insert(sample, machine.Sample), Is.True);
            Assert.That(genetics.TryStart(pod), Is.True);
            genetics.Advance(pod, machine.AnalysisSeconds);
            Assert.That(em.System<SharedMaterialStorageSystem>()
                .TryChangeMaterialAmount(pod, "Biomass", machine.BiomassCost), Is.True);
            Assert.That(genetics.TryStart(pod), Is.True);
            genetics.Advance(pod, machine.GrowthSeconds);
            var grown = genetics.TryRelease(pod);
            Assert.That(grown, Is.Not.Null);
            Assert.That(em.GetComponent<HumanoidAppearanceComponent>(grown!.Value).Sex, Is.EqualTo(Sex.Female));
            Assert.That(em.GetComponent<HumanoidAppearanceComponent>(grown.Value).Height,
                Is.EqualTo(1.1f).Within(0.001f));
            Assert.That(em.GetComponent<DnaComponent>(grown.Value).DNA, Is.EqualTo(donorDna));
            Assert.That(em.GetComponent<MindContainerComponent>(grown.Value).Mind, Is.Null);
            Assert.That(body.GetBodyChildren(grown.Value).SelectMany(p => body.GetPartOrgans(p.Id))
                .Any(o => em.HasComponent<BrainComponent>(o.Id)), Is.False);
        });
    }

    [Test]
    public async Task SampleGrowthPowerAndTransplantBody()
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
            var pos = new EntityCoordinates(grid.Owner, 0.5f, 0.5f);
            var donor = em.SpawnEntity("MobHuman", pos);
            em.System<SharedHumanoidAppearanceSystem>().SetSex(donor, Sex.Female);
            var sample = em.SpawnEntity("GeneticSampleCartridge", pos);
            var genetics = em.System<GeneticBodySystem>();
            Assert.That(genetics.Collect(sample, donor), Is.True);
            Assert.That(genetics.Collect(sample, donor), Is.False, "A sample cannot be overwritten.");
            var data = em.GetComponent<GeneticSampleComponent>(sample);
            var dna = data.Dna;
            em.System<SharedHumanoidAppearanceSystem>().SetSex(donor, Sex.Male);
            Assert.That(data.Sex, Is.EqualTo(Sex.Female), "Samples are immutable snapshots.");

            var rotten = em.SpawnEntity("MobHuman", pos);
            em.AddComponent<RottingComponent>(rotten);
            var empty = em.SpawnEntity("GeneticSampleCartridge", pos);
            Assert.That(genetics.Collect(empty, rotten), Is.False);

            var pod = em.SpawnEntity("GeneticBodyGrower", pos);
            Assert.That(em.GetComponent<TransformComponent>(pod).Anchored, Is.True);
            Assert.That(em.GetComponent<TransformComponent>(pod).Anchored, Is.True);
            var machine = em.GetComponent<GeneticBodyGrowerComponent>(pod);
            var power = em.GetComponent<ApcPowerReceiverComponent>(pod);
            power.Powered = true;
            Assert.That(em.System<SharedContainerSystem>().Insert(sample, machine.Sample), Is.True);
            Assert.That(genetics.TryStart(pod), Is.True);
            Assert.That(genetics.TryStart(pod), Is.False);
            genetics.Advance(pod, machine.AnalysisSeconds);
            Assert.That(data.Analyzed, Is.True);
            Assert.That(machine.Stage, Is.EqualTo(GeneticGrowthStage.Idle));
            Assert.That(genetics.TryStart(pod), Is.False, "Growing requires biomass.");
            var materials = em.System<SharedMaterialStorageSystem>();
            Assert.That(materials.TryChangeMaterialAmount(pod, "Biomass", machine.BiomassCost * 2), Is.True);
            Assert.That(genetics.TryStart(pod), Is.True);
            Assert.That(materials.GetMaterialAmount(pod, "Biomass"), Is.EqualTo(machine.BiomassCost));
            Assert.That(genetics.TryRelease(pod), Is.Null);
            var remaining = machine.Remaining;
            power.Powered = false;
            genetics.Advance(pod, 60);
            Assert.That(machine.Remaining, Is.EqualTo(remaining), "Power loss pauses growth.");
            power.Powered = true;
            genetics.Advance(pod, remaining);
            Assert.That(machine.Stage, Is.EqualTo(GeneticGrowthStage.Ready));
            var result = genetics.TryRelease(pod);
            Assert.That(result, Is.Not.Null);
            var grown = result!.Value;
            Assert.That(em.GetComponent<HumanoidAppearanceComponent>(grown).Sex, Is.EqualTo(Sex.Female));
            Assert.That(em.GetComponent<HumanoidAppearanceComponent>(grown).Species.Id, Is.EqualTo("Human"));
            Assert.That(em.GetComponent<DnaComponent>(grown).DNA, Is.Not.EqualTo(dna),
                "Identity restoration belongs to the donor brain, not a sample.");
            Assert.That(em.GetComponent<MindContainerComponent>(grown).Mind, Is.Null);
            var body = em.System<SharedBodySystem>();
            Assert.That(body.GetBodyChildren(grown).SelectMany(p => body.GetPartOrgans(p.Id))
                .Any(o => em.HasComponent<BrainComponent>(o.Id)), Is.False);
            Assert.That(genetics.TryRelease(pod), Is.Null, "One cycle must not duplicate bodies.");
            Assert.That(machine.Sample.ContainedEntity, Is.EqualTo(sample));

            Assert.That(genetics.TryStart(pod), Is.True);
            power.Powered = false;
            genetics.Advance(pod, machine.PowerFailureSeconds);
            Assert.That(machine.Stage, Is.EqualTo(GeneticGrowthStage.Idle));
            Assert.That(materials.GetMaterialAmount(pod, "Biomass"), Is.Zero, "Failed cultures do not refund biomass.");
            Assert.That(data.Analyzed, Is.True);
            Assert.That(genetics.TryRelease(pod), Is.Null);

            // A removed cartridge invalidates the cycle, even if removal bypasses verbs.
            power.Powered = true;
            materials.TryChangeMaterialAmount(pod, "Biomass", machine.BiomassCost);
            Assert.That(genetics.TryStart(pod), Is.True);
            Assert.That(em.System<SharedContainerSystem>().Remove(sample, machine.Sample), Is.True);
            Assert.That(machine.Stage, Is.EqualTo(GeneticGrowthStage.Idle));

            // The window is useful before and after inserting a sample.
            Assert.That(genetics.BuildUiState(pod).CanGrow, Is.False);
            Assert.That(em.System<SharedContainerSystem>().Insert(sample, machine.Sample), Is.True);
            Assert.That(genetics.BuildUiState(pod).Products, Does.Contain(GeneticProduct.Heart));
            Assert.That(genetics.SelectProduct(pod, (GeneticProduct) 255), Is.False);
            Assert.That(genetics.OrganPrototype(new GeneticSampleComponent { Species = "SlimePerson" },
                GeneticProduct.Heart), Is.Null, "Slimes must not be offered human hearts.");
            Assert.That(genetics.OrganPrototype(new GeneticSampleComponent { Species = "SlimePerson" },
                GeneticProduct.Lungs), Is.EqualTo("OrganSlimeLungs"));

            foreach (var product in genetics.BuildUiState(pod).Products.Where(p => p != GeneticProduct.Body))
            {
                Assert.That(genetics.SelectProduct(pod, product), Is.True);
                Assert.That(genetics.BuildUiState(pod).Cost, Is.EqualTo(machine.OrganBiomassCost));
                var expected = genetics.OrganPrototype(data, product);
                materials.TryChangeMaterialAmount(pod, "Biomass", machine.OrganBiomassCost);
                var before = materials.GetMaterialAmount(pod, "Biomass");
                Assert.That(genetics.TryStart(pod), Is.True);
                Assert.That(machine.Remaining, Is.EqualTo(machine.OrganGrowthSeconds));
                Assert.That(genetics.SelectProduct(pod, GeneticProduct.Body), Is.False);
                Assert.That(genetics.BuildUiState(pod).CanSelect, Is.False);
                genetics.Advance(pod, machine.OrganGrowthSeconds / 2);
                Assert.That(genetics.BuildUiState(pod).Progress, Is.EqualTo(0.5f).Within(0.001));
                genetics.Advance(pod, machine.OrganGrowthSeconds / 2);
                Assert.That(genetics.BuildUiState(pod).CanRelease, Is.True);
                var organ = genetics.TryRelease(pod);
                Assert.That(organ, Is.Not.Null);
                Assert.That(em.GetComponent<MetaDataComponent>(organ!.Value).EntityPrototype!.ID, Is.EqualTo(expected));
                Assert.That(em.HasComponent<BrainComponent>(organ.Value), Is.False);
                Assert.That(materials.GetMaterialAmount(pod, "Biomass"),
                    Is.EqualTo(before - machine.OrganBiomassCost));
                Assert.That(genetics.TryRelease(pod), Is.Null);
            }
        });
    }
}

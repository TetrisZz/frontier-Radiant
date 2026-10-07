using System.Linq;
using Content.Shared.Storage.EntitySystems;
using Content.Server._radiant.Addictions;
using Content.Server._radiant.Medical.Genetics;
using Content.Shared.Abilities;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Electrocution;
using Content.Shared.Overlays;
using Content.Shared.Lathe;
using Content.Shared.Weapons.Melee;
using Content.Shared._radiant.Abilities.Shadowkin;
using Content.Shared._radiant.Medical.Genetics;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class GeneticAdaptationTest
{
    [Test]
    public async Task BenefitsComplicationsAndTargetedRemoval()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var em = server.ResolveDependency<IEntityManager>();
        var prototypes = server.ResolveDependency<IPrototypeManager>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            var pos = new EntityCoordinates(map, 0, 0);
            var genetics = em.System<GeneticModificationSystem>();
            var melee = em.System<SharedMeleeWeaponSystem>();
            var patient = em.SpawnEntity("MobHuman", pos);
            var fabricator = em.SpawnEntity("MedicalTechFab", pos);
            var recipes = em.System<SharedLatheSystem>().GetAllPossibleRecipes(em.GetComponent<LatheComponent>(fabricator));
            Assert.That(recipes.Any(recipe => recipe.Id == "GeneticSampleCartridge"), Is.True);
            Assert.That(recipes.Any(recipe => recipe.Id == "GeneticResearchDisk"), Is.True);
            Assert.That(recipes.Any(recipe => recipe.Id == "GeneticResearchDiskBox"), Is.True);
            var archiveCase = em.SpawnEntity("GeneticResearchDiskBox", pos);
            var storage = em.System<SharedStorageSystem>();
            Assert.That(storage.Insert(archiveCase, em.SpawnEntity("GeneticSampleCartridge", pos), out _), Is.False);
            for (var i = 0; i < 8; i++)
                Assert.That(storage.Insert(archiveCase, em.SpawnEntity("GeneticResearchDisk", pos), out _), Is.True);
            Assert.That(storage.Insert(archiveCase, em.SpawnEntity("GeneticResearchDisk", pos), out _), Is.False);
            var genes = prototypes.EnumeratePrototypes<GeneticModificationPrototype>().ToArray();
            var chances = genes.ToDictionary(gene => gene.ID, gene => gene.ComplicationChance);
            try
            {
                foreach (var gene in genes) gene.ComplicationChance = 0;
                bool Inject(string id) => genetics.Apply(em.SpawnEntity(id, pos), patient);
                var baseDamage = melee.GetDamage(patient, patient).GetTotal().Float();
                Assert.That(Inject("GeneticStrengthInjector"), Is.True);
                Assert.That(melee.GetDamage(patient, patient).GetTotal().Float(), Is.EqualTo(baseDamage));
                genetics.Advance(patient, 120);
                Assert.That(melee.GetDamage(patient, patient).GetTotal().Float(), Is.EqualTo(baseDamage * 1.25f).Within(.02));
                var profile = em.GetComponent<GeneticProfileComponent>(patient);
                Assert.That(profile.Complications, Is.Empty);

                // A flaw belongs to its complex and supersedes the positive strength multiplier.
                profile.Complications["RadiantStrength"] = "weakness";
                genetics.Advance(patient, 1);
                var effects = em.GetComponent<GeneticBodyEffectsComponent>(patient);
                Assert.That(effects.UnarmedMultiplier, Is.EqualTo(.5f));
                Assert.That(effects.Speed, Is.EqualTo(.9f));
                Assert.That(Inject("GeneticStrengthRemover"), Is.True);
                Assert.That(effects.UnarmedMultiplier, Is.EqualTo(1));
                Assert.That(effects.Speed, Is.EqualTo(1));
                Assert.That(profile.Complications, Is.Empty);

                // Do not remove natural ultraviolet vision when the gene is suppressed.
                var naturalVision = em.EnsureComponent<UltraVisionComponent>(patient);
                Assert.That(Inject("GeneticUltravisionInjector"), Is.True);
                genetics.Advance(patient, 120);
                Assert.That(em.HasComponent<ShadowkinUltravisionTintComponent>(patient), Is.True);
                profile.Complications["RadiantUltravision"] = "monochromacy";
                genetics.Advance(patient, 1);
                Assert.That(em.HasComponent<BlackAndWhiteOverlayComponent>(patient), Is.True);
                Assert.That(Inject("GeneticUltravisionRemover"), Is.True);
                Assert.That(em.GetComponent<UltraVisionComponent>(patient), Is.SameAs(naturalVision));
                Assert.That(em.HasComponent<ShadowkinUltravisionTintComponent>(patient), Is.False);
                Assert.That(em.HasComponent<BlackAndWhiteOverlayComponent>(patient), Is.False);

                Assert.That(Inject("GeneticInsulationInjector"), Is.True);
                genetics.Advance(patient, 120);
                Assert.That(effects.Insulated, Is.True);
                Assert.That(Inject("GeneticStrengthInjector"), Is.True);
                genetics.Advance(patient, 120);
                Assert.That(Inject("GeneticStrengthRemover"), Is.True);
                Assert.That(effects.Insulated, Is.True, "A different complex must remain active.");
                Assert.That(Inject("GeneticInsulationRemover"), Is.True);
                Assert.That(effects.Insulated, Is.False);

                Assert.That(Inject("GeneticSobrietyInjector"), Is.True);
                genetics.Advance(patient, 120);
                var addiction = em.System<AddictionSystem>();
                var nicotine = prototypes.Index<ReagentPrototype>("Nicotine");
                addiction.RegisterDose(patient, nicotine, 10);
                var state = em.GetComponent<AddictionComponent>(patient).Groups["nicotine"];
                Assert.That(state.Dependence, Is.Zero);
                Assert.That(state.Tolerance, Is.Zero);
                Assert.That(state.Intoxication, Is.GreaterThan(0), "Poisoning is not suppressed.");
                Assert.That(Inject("GeneticSobrietyRemover"), Is.True);
                addiction.RegisterDose(patient, nicotine, 10);
                Assert.That(state.Dependence, Is.GreaterThan(0));

                // Guarantee the roll to test the production path without a flaky random assertion.
                prototypes.Index<GeneticModificationPrototype>("RadiantStrength").ComplicationChance = 1;
                Assert.That(Inject("GeneticStrengthInjector"), Is.True);
                Assert.That(profile.Complications.Count, Is.EqualTo(1));
                Assert.That(profile.Complications.ContainsKey("RadiantStrength"), Is.True);
                Assert.That(Inject("GeneticStrengthRemover"), Is.True);
                Assert.That(profile.Complications, Is.Empty);
            }
            finally
            {
                foreach (var gene in genes) gene.ComplicationChance = chances[gene.ID];
            }
        });
    }
}

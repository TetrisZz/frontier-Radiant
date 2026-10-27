using System.Linq;
using Content.Server.Power.Components;
using Content.Server._radiant.Medical.Genetics;
using Content.Server._radiant.Medical.Virology;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.FixedPoint;
using Content.Shared.Forensics.Components;
using Content.Shared.Humanoid;
using Content.Shared.Materials;
using Content.Shared._radiant.Medical.Genetics;
using Content.Shared._radiant.Medical.Virology;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Medical;

public sealed class MedicalLaboratoryTest
{
    [Test]
    public async Task VaultDeduplicatesBiologyAndReservesOnePersonalKitAcrossMachines()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var archive = em.System<DnaVaultSystem>();
                var genetics = em.System<GeneticBodySystem>();
                var materials = em.System<SharedMaterialStorageSystem>();
                var probe = em.SpawnEntity("BiodiversityProbe", map.GridCoords);
                EntityUid Patient(string dna)
                {
                    var patient = em.SpawnEntity("MobHuman", map.GridCoords);
                    em.GetComponent<DnaComponent>(patient).DNA = dna;
                    em.System<SharedHumanoidAppearanceSystem>().SetSex(patient, Sex.Female);
                    return patient;
                }
                var patient = Patient("vault-patient");
                var clone = Patient("vault-patient");
                Assert.That(archive.Scan(probe, patient), Is.True);
                Assert.That(archive.Scan(probe, clone), Is.False, "Cloning does not create a new DNA record.");
                foreach (var donor in new[] { Patient("vault-2"), Patient("vault-3") })
                    Assert.That(archive.Scan(probe, donor), Is.True);
                foreach (var id in new[] { "FoodTomato", "FoodPotato", "FoodCarrot", "MobMouse", "MobChicken", "MobGoat" })
                    Assert.That(archive.Scan(probe, em.SpawnEntity(id, map.GridCoords)), Is.True, id);
                Assert.That(archive.Scan(probe, em.SpawnEntity("MobMouse1", map.GridCoords)), Is.False,
                    "A color variant is the same animal species.");
                var records = em.GetComponent<BiodiversityProbeComponent>(probe).Records.ToArray();
                EntityUid Vault(EntityUid donor)
                {
                    var vault = em.SpawnEntity("DnaVault", map.GridCoords);
                    em.GetComponent<DnaVaultComponent>(vault).RandomizeGoals = false;
                    em.GetComponent<ApcPowerReceiverComponent>(vault).Powered = true;
                    var upload = em.SpawnEntity("BiodiversityProbe", map.GridCoords);
                    em.GetComponent<BiodiversityProbeComponent>(upload).Records.AddRange(records);
                    Assert.That(archive.Upload(vault, upload), Is.EqualTo(9));
                    Assert.That(em.GetComponent<BiodiversityProbeComponent>(upload).Records, Is.Empty);
                    Assert.That(archive.Upload(vault, upload), Is.Zero);
                    var cartridge = em.SpawnEntity("GeneticSampleCartridge", map.GridCoords);
                    Assert.That(genetics.Collect(cartridge, donor), Is.True);
                    em.GetComponent<GeneticSampleComponent>(cartridge).Analyzed = true;
                    Assert.That(archive.Insert(vault, cartridge), Is.True);
                    materials.TryChangeMaterialAmount(vault, "Biomass", 120);
                    return vault;
                }
                var first = Vault(patient);
                var second = Vault(clone);
                var state = archive.BuildState(first);
                Assert.That(state.Rewards.Count, Is.EqualTo(2));
                Assert.That(archive.BuildState(second).Rewards.Keys, Is.EquivalentTo(state.Rewards.Keys));
                Assert.That(archive.Prepare(first, "GeneticStrengthInjector"), Is.False);
                Assert.That(archive.Prepare(first, state.Rewards.Keys.First()), Is.True);
                Assert.That(archive.Prepare(second, state.Rewards.Keys.Last()), Is.False, "Global reservation prevents racing two archives.");
                em.GetComponent<ApcPowerReceiverComponent>(first).Powered = false;
                archive.Advance(first, 1000);
                Assert.That(em.GetComponent<DnaVaultComponent>(first).Order, Is.Not.Null);
                em.GetComponent<ApcPowerReceiverComponent>(first).Powered = true;
                archive.Advance(first, 180);
                Assert.That(em.GetComponent<DnaVaultComponent>(first).Records, Is.Empty);
                Assert.That(em.GetComponent<DnaVaultComponent>(second).Records.Count, Is.EqualTo(9),
                    "Completing one machine must not clear another machine's collection.");
                Assert.That(archive.BuildState(second).CanPrepare, Is.False);
                Assert.That(archive.Prepare(second, state.Rewards.Keys.First()), Is.False);
                var doses = em.EntityQuery<GeneticTherapyComponent>().Where(d => d.Dna == "vault-patient").ToArray();
                Assert.That(doses.Length, Is.EqualTo(2));
                Assert.That(doses.Count(d => d.Remove), Is.EqualTo(1));
                Assert.That(doses.All(d => d.Species == "Human" && d.Variant == GeneticTherapyVariant.Standard), Is.True);
                Assert.That(materials.GetMaterialAmount(first, "Biomass"), Is.EqualTo(60));
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task EachPrintedKitConsumesItsCollectionAndAssignsNewDistinctRequirements()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var archive = em.System<DnaVaultSystem>();
                var uid = em.SpawnEntity("DnaVault", map.GridCoords);
                var vault = em.GetComponent<DnaVaultComponent>(uid);
                var power = em.GetComponent<ApcPowerReceiverComponent>(uid);
                power.Powered = true;
                em.System<SharedMaterialStorageSystem>().TryChangeMaterialAmount(uid, "Biomass", 500);
                var probe = em.SpawnEntity("BiodiversityProbe", map.GridCoords);
                var memory = em.GetComponent<BiodiversityProbeComponent>(probe);
                for (var cycle = 0; cycle < 4; cycle++)
                {
                    var before = archive.BuildState(uid);
                    var goals = new[] { before.HumanGoal, before.PlantGoal, before.AnimalGoal };
                    Assert.That(goals.All(n => n is >= 2 and <= 5), Is.True);
                    Assert.That(goals.Distinct().Count(), Is.EqualTo(3));
                    foreach (var (category, count) in new[] { ("human", goals[0]), ("plant", goals[1]), ("animal", goals[2]) })
                    for (var i = 0; i < count; i++)
                        memory.Records.Add(new BiodiversityRecord { Key = category + i, Category = category, Label = category + i });
                    Assert.That(archive.Upload(uid, probe), Is.EqualTo(goals.Sum()));
                    Assert.That(memory.Records, Is.Empty, "Uploaded records cannot be replayed after the reset.");
                    Assert.That(archive.Upload(uid, probe), Is.Zero);
                    var cartridge = em.SpawnEntity("GeneticSampleCartridge", map.GridCoords);
                    var sample = em.GetComponent<GeneticSampleComponent>(cartridge);
                    sample.Species = "Human";
                    sample.Dna = "collection-patient-" + cycle;
                    sample.Donor = sample.Dna;
                    sample.Analyzed = sample.TherapyCompatible = true;
                    Assert.That(archive.Insert(uid, cartridge), Is.True);
                    var ready = archive.BuildState(uid);
                    Assert.That(archive.Prepare(uid, ready.Rewards.Keys.First()), Is.True);
                    Assert.That(vault.Records.Count, Is.EqualTo(goals.Sum()), "Starting an order must not discard samples.");
                    if (cycle == 0)
                    {
                        power.Powered = false;
                        archive.Advance(uid, 1000);
                        Assert.That(vault.Records.Count, Is.EqualTo(goals.Sum()), "A paused order must keep its collection.");
                        power.Powered = true;
                    }
                    archive.Advance(uid, vault.PreparationSeconds);
                    var after = archive.BuildState(uid);
                    Assert.That(new[] { after.Humans, after.Plants, after.Animals }, Is.All.Zero);
                    var next = new[] { after.HumanGoal, after.PlantGoal, after.AnimalGoal };
                    Assert.That(next.SequenceEqual(goals), Is.False);
                    Assert.That(next.Distinct().Count(), Is.EqualTo(3));
                    Assert.That(next.All(n => n is >= 2 and <= 5), Is.True);
                    Assert.That(archive.Upload(uid, probe), Is.Zero);
                    Assert.That(after.CanPrepare, Is.False);
                    em.System<SharedContainerSystem>().Remove(cartridge, vault.Sample);
                    em.DeleteEntity(cartridge);
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task StudiedStrainsSurviveLiquidTransferAndRemainTreatableByTheirFamily()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var lab = em.System<VirologyLabSystem>();
                var virology = em.System<VirologySystem>();
                var materials = em.System<SharedMaterialStorageSystem>();
                var patient = em.SpawnEntity("MobHuman", map.GridCoords);
                Assert.That(virology.Expose(patient, "Hematic", 100), Is.True);
                var sample = em.SpawnEntity("DiseaseSwab", map.GridCoords);
                Assert.That(lab.Collect(sample, patient), Is.True);
                var analyzer = em.SpawnEntity("DiseaseDiagnoser", map.GridCoords);
                em.GetComponent<ApcPowerReceiverComponent>(analyzer).Powered = true;
                Assert.That(lab.Insert(analyzer, sample), Is.True);
                Assert.That(lab.Start(analyzer, VirologyCommand.Analyze), Is.True);
                lab.Advance(analyzer, 30);
                var state = lab.BuildState(analyzer);
                Assert.That(state.Symptoms.Keys, Does.Contain("Fatigue"));
                Assert.That(lab.Start(analyzer, VirologyCommand.Study), Is.True);
                Assert.That(lab.Start(analyzer, VirologyCommand.Study), Is.False);
                lab.Advance(analyzer, 90);
                Assert.That(lab.BuildState(analyzer).Symptoms.Count, Is.EqualTo(2));
                var sampleData = em.GetComponent<VirologySampleComponent>(sample);
                while (lab.Start(analyzer, VirologyCommand.Study)) lab.Advance(analyzer, 90);
                Assert.That(sampleData.KnownSymptoms, Does.Contain("Persistent"));
                em.System<SharedContainerSystem>().Remove(sample, em.GetComponent<VirologyMachineComponent>(analyzer).Sample);
                var sequencer = em.SpawnEntity("VirologySequencer", map.GridCoords);
                em.GetComponent<ApcPowerReceiverComponent>(sequencer).Powered = true;
                Assert.That(lab.Insert(sequencer, sample), Is.True);
                materials.TryChangeMaterialAmount(sequencer, "Biomass", 50);
                lab.BuildState(sequencer);
                Assert.That(lab.ToggleSymptom(sequencer, "Cough"), Is.False, "Blood transmission cannot acquire airborne coughing.");
                Assert.That(lab.ToggleSymptom(sequencer, "Accelerated"), Is.True);
                Assert.That(lab.ToggleSymptom(sequencer, "Persistent"), Is.True);
                Assert.That(lab.ToggleSymptom(sequencer, "Quiet"), Is.False, "Danger budget is server enforced.");
                Assert.That(lab.Start(sequencer, VirologyCommand.Culture), Is.True);
                Assert.That(lab.ToggleSymptom(sequencer, "Accelerated"), Is.False, "A running batch cannot be edited.");
                var machine = em.GetComponent<VirologyMachineComponent>(sequencer);
                var expected = VirologyStrains.Canonical("Hematic", ["Persistent", "Accelerated"]);
                Assert.That(machine.PendingProfile, Is.EqualTo(expected));
                lab.Advance(sequencer, 120);
                var bottle = em.EntityQuery<SolutionContainerManagerComponent>().Select(c => c.Owner)
                    .Single(uid => em.System<SharedSolutionContainerSystem>().TryGetSolution(uid, "drink", out _, out var solution)
                        && solution.Contents.Any(r => r.Reagent.Data?.OfType<VirologyStrainData>().Any(s => s.Strain == expected) == true));
                var swab = em.SpawnEntity("DiseaseSwab", map.GridCoords);
                Assert.That(lab.Collect(swab, bottle), Is.True);
                Assert.That(em.GetComponent<VirologySampleComponent>(swab).Diseases, Is.EquivalentTo(new[] { expected }));
                em.System<SharedSolutionContainerSystem>().TryGetSolution(bottle, "drink", out _, out var contents);
                var recipient = em.SpawnEntity("MobHuman", map.GridCoords);
                em.System<ReactiveSystem>().DoEntityReaction(recipient, contents!.Clone(), ReactionMethod.Injection);
                Assert.That(em.GetComponent<VirologyCarrierComponent>(recipient).Infections.Keys, Does.Contain(expected));
                Assert.That(virology.Expose(recipient, "Hematic", 100), Is.False, "Variants cannot stack infections from the same family.");
                virology.Advance(recipient, virology.Disease(expected).Incubation);
                var blood = new Solution();
                blood.AddReagent("Blood", 15);
                virology.Contaminate(recipient, blood, "blood");
                Assert.That(blood.Volume, Is.EqualTo(FixedPoint2.New(15)));
                Assert.That(blood.Contents.Any(r => r.Reagent.Data?.OfType<VirologyStrainData>().Any(s => s.Strain == expected) == true), Is.True);
                Assert.That(virology.ApplyDose(recipient, "Hematic", false), Is.True);
                virology.Advance(recipient, 120);
                Assert.That(em.GetComponent<VirologyCarrierComponent>(recipient).Infections, Is.Not.Empty,
                    "A family dose must still honor the persistent strain's displayed treatment time.");
                virology.Advance(recipient, virology.Disease(expected).TreatmentSeconds - 120);
                Assert.That(em.GetComponent<VirologyCarrierComponent>(recipient).Infections, Is.Empty);
                var protectedPatient = em.SpawnEntity("MobHuman", map.GridCoords);
                Assert.That(virology.ApplyDose(protectedPatient, "Hematic", true), Is.True);
                em.System<ReactiveSystem>().DoEntityReaction(protectedPatient, contents.Clone(), ReactionMethod.Injection);
                Assert.That(em.GetComponent<VirologyCarrierComponent>(protectedPatient).Infections, Is.Empty);
                Assert.That(VirologyStrains.TryResolve(pair.Server.ProtoMan, "Hematic~Unknown", out _), Is.False);
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task TherapyVariantsRespectLoadExpiryAndRecoveryWithoutInstantHealing()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var genetics = em.System<GeneticModificationSystem>();
                var patient = em.SpawnEntity("MobHuman", map.GridCoords);
                var temporary = em.SpawnEntity("GeneticRegenerationInjector", map.GridCoords);
                em.GetComponent<GeneticTherapyComponent>(temporary).Variant = GeneticTherapyVariant.Temporary;
                Assert.That(genetics.Apply(temporary, patient), Is.True);
                var stable = em.SpawnEntity("GeneticHematopoiesisInjector", map.GridCoords);
                em.GetComponent<GeneticTherapyComponent>(stable).Variant = GeneticTherapyVariant.Stable;
                Assert.That(genetics.Apply(stable, patient), Is.False, "55 + 40 + 10 exceeds the common cap.");
                var profile = em.GetComponent<GeneticProfileComponent>(patient);
                Assert.That(genetics.Load(profile), Is.EqualTo(55));
                genetics.Advance(patient, 180);
                Assert.That(profile.Lifetimes["RadiantRegeneration"], Is.EqualTo(1200));
                genetics.Advance(patient, 1200);
                Assert.That(profile.Modifications, Is.Empty);
                Assert.That(genetics.Apply(stable, patient), Is.False, "Expiry also requires recovery.");
                genetics.Advance(patient, 180);
                Assert.That(genetics.Apply(stable, patient), Is.True);
                Assert.That(genetics.Load(profile), Is.EqualTo(50));
                Assert.That(GeneticTherapyBalance.Risk(.15f, GeneticTherapyVariant.Stable), Is.EqualTo(.0375f).Within(.0001));
                var recovering = em.SpawnEntity("MobHuman", map.GridCoords);
                Assert.That(genetics.Apply(em.SpawnEntity("GeneticRegenerationInjector", map.GridCoords), recovering), Is.True);
                genetics.Advance(recovering, 180);
                var damage = new Content.Shared.Damage.DamageSpecifier();
                damage.DamageDict["Blunt"] = FixedPoint2.New(10);
                damage.DamageDict["Poison"] = FixedPoint2.New(5);
                em.System<Content.Shared.Damage.DamageableSystem>().TryChangeDamage(recovering, damage, ignoreResistances: true);
                var damaged = em.GetComponent<Content.Shared.Damage.DamageableComponent>(recovering);
                genetics.Advance(recovering, 29);
                Assert.That(damaged.Damage.DamageDict["Blunt"].Float(), Is.EqualTo(10));
                genetics.Advance(recovering, 1);
                genetics.Advance(recovering, 60);
                Assert.That(damaged.Damage.DamageDict["Blunt"].Float(), Is.EqualTo(9.1f).Within(.01));
                Assert.That(damaged.Damage.DamageDict["Poison"].Float(), Is.EqualTo(5), "Genetics does not replace toxin medicine.");
                em.System<Content.Shared.Mobs.Systems.MobStateSystem>().ChangeMobState(recovering, Content.Shared.Mobs.MobState.Critical);
                genetics.Advance(recovering, 60);
                Assert.That(damaged.Damage.DamageDict["Blunt"].Float(), Is.EqualTo(9.1f).Within(.01), "Critical patients need medical intervention.");
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}

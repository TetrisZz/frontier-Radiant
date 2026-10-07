using System.Linq;
using Content.Server.Power.Components;
using Content.Server._radiant.Medical.Virology;
using Content.Shared.Body.Components;
using Content.Shared.Body.Events;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Humanoid;
using Content.Shared.Inventory;
using Content.Shared.Clothing.Components;
using Content.Shared.Materials;
using Content.Shared._radiant.Medical.Virology;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class VirologyTest
{
    [Test]
    public async Task ExposureRoutesTreatmentAndTransplantation()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var em = server.ResolveDependency<IEntityManager>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            var pos = new EntityCoordinates(map, 0, 0);
            var system = em.System<VirologySystem>();
            var patient = em.SpawnEntity("MobHuman", pos);
            Assert.That(system.Expose(patient, "StationFever", 50), Is.False);
            system.Advance(patient, 10);
            Assert.That(system.Expose(patient, "StationFever", 50), Is.False, "Small exposures decay.");
            Assert.That(system.Expose(patient, "StationFever", 10), Is.True);
            Assert.That(system.ApplyDose(patient, "StationFever", true), Is.False, "Vaccines do not treat active disease.");
            var carrier = em.GetComponent<VirologyCarrierComponent>(patient);
            Assert.That(system.ApplyDose(patient, "Enteric", false), Is.False);
            system.Advance(patient, 180);
            Assert.That(em.GetComponent<VirologySymptomsComponent>(patient).Severity, Is.EqualTo(1));
            Assert.That(em.GetComponent<VirologySymptomsComponent>(patient).SpeedMultiplier, Is.LessThan(1));
            Assert.That(system.ApplyDose(patient, "StationFever", false), Is.True);
            Assert.That(em.GetComponent<VirologySymptomsComponent>(patient).SpeedMultiplier, Is.EqualTo(1));
            Assert.That(system.ApplyDose(patient, "StationFever", false), Is.False, "Cannot stack treatment.");
            system.Advance(patient, 119);
            Assert.That(carrier.Infections.ContainsKey("StationFever"), Is.True);
            system.Advance(patient, 1);
            Assert.That(carrier.Infections, Is.Empty);
            Assert.That(em.GetComponent<VirologySymptomsComponent>(patient).Severity, Is.Zero);
            Assert.That(system.Expose(patient, "StationFever", 1000), Is.False);
            system.Advance(patient, 1201);
            Assert.That(system.Expose(patient, "StationFever", 100), Is.True);

            var exposed = em.SpawnEntity("MobHuman", pos);
            var reactions = em.System<ReactiveSystem>();
            var liquid = new Solution();
            liquid.AddReagent("CultureHematic", FixedPoint2.New(1));
            reactions.DoEntityReaction(exposed, liquid, ReactionMethod.Touch);
            reactions.DoEntityReaction(exposed, liquid, ReactionMethod.Ingestion);
            Assert.That(em.HasComponent<VirologyCarrierComponent>(exposed), Is.False, "Bloodborne route is not touch or food.");
            reactions.DoEntityReaction(exposed, liquid, ReactionMethod.Injection);
            Assert.That(em.GetComponent<VirologyCarrierComponent>(exposed).Infections.ContainsKey("Hematic"), Is.True);

            var diner = em.SpawnEntity("MobHuman", pos);
            liquid = new Solution();
            liquid.AddReagent("CultureEnteric", FixedPoint2.New(1));
            reactions.DoEntityReaction(diner, liquid, ReactionMethod.Ingestion);
            Assert.That(em.GetComponent<VirologyCarrierComponent>(diner).Infections.ContainsKey("Enteric"), Is.True);
            system.Advance(diner, 180);
            var leftovers = new Solution();
            leftovers.AddReagent("Water", FixedPoint2.New(10));
            system.Contaminate(diner, leftovers, "ingestion");
            Assert.That(leftovers.Volume, Is.EqualTo(FixedPoint2.New(10)));
            Assert.That(leftovers.GetTotalPrototypeQuantity("CultureEnteric"), Is.GreaterThan(FixedPoint2.Zero));
            system.Advance(exposed, 180);
            var regeneration = new BloodRegenerationEvent(FixedPoint2.New(1));
            em.EventBus.RaiseLocalEvent(exposed, ref regeneration);
            Assert.That(regeneration.Amount.Float(), Is.EqualTo(0.02f).Within(0.001));
            var blood = new Solution();
            blood.AddReagent("Blood", FixedPoint2.New(5));
            system.Contaminate(exposed, blood, "blood");
            Assert.That(blood.Volume, Is.EqualTo(FixedPoint2.New(5)));
            Assert.That(blood.GetTotalPrototypeQuantity("CultureHematic"), Is.GreaterThan(FixedPoint2.Zero));
            Assert.That(system.ApplyDose(exposed, "Hematic", false), Is.True);
            regeneration = new BloodRegenerationEvent(FixedPoint2.New(1));
            em.EventBus.RaiseLocalEvent(exposed, ref regeneration);
            Assert.That(regeneration.Amount, Is.EqualTo(FixedPoint2.New(1)));

            var mask = em.SpawnEntity("ClothingMaskBreathMedical", pos);
            Assert.That(em.System<InventorySystem>().TryEquip(patient, mask, "mask", force: true), Is.True);
            Assert.That(system.DropletProtection(patient), Is.EqualTo(0.3f));
            em.System<Content.Shared.Clothing.EntitySystems.MaskSystem>().SetToggled(mask, true, force: true);
            Assert.That(system.DropletProtection(patient), Is.EqualTo(1));

            var body = em.System<SharedBodySystem>();
            var organ = body.GetBodyChildren(exposed).SelectMany(p => body.GetPartOrgans(p.Id)).First().Id;
            Assert.That(body.RemoveOrgan(organ), Is.True);
            Assert.That(em.GetComponent<VirologyOrganComponent>(organ).Diseases, Does.Contain("Hematic"));
            var recipient = em.SpawnEntity("MobHuman", pos);
            var added = new OrganAddedToBodyEvent(recipient, recipient);
            em.EventBus.RaiseLocalEvent(organ, ref added);
            Assert.That(em.GetComponent<VirologyCarrierComponent>(recipient).Infections.ContainsKey("Hematic"), Is.True);

            var slime = em.SpawnEntity("MobHuman", pos);
            em.GetComponent<HumanoidAppearanceComponent>(slime).Species = "SlimePerson";
            Assert.That(system.Expose(slime, "Enteric", 1000), Is.False);
            var monkey = em.SpawnEntity("MobMonkey", pos);
            Assert.That(system.Susceptible(monkey), Is.True);
            Assert.That(system.Expose(monkey, "StationFever", 100), Is.True);
            var animalSample = em.SpawnEntity("DiseaseSwab", pos);
            Assert.That(em.System<VirologyLabSystem>().Collect(animalSample, monkey), Is.True);
            Assert.That(em.GetComponent<VirologySampleComponent>(animalSample).Diseases, Does.Contain("StationFever"));
            Assert.That(system.Expose(patient, "Enteric", 100), Is.True);
            Assert.That(system.Expose(patient, "Hematic", 100), Is.False, "At most two concurrent infections.");
        });
    }

    [Test]
    public async Task ExistingMachinesAnalyzeAndProduceWithoutFreeOrDuplicateDoses()
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
            var diagnoser = em.SpawnEntity("DiseaseDiagnoser", pos);
            var vaccinator = em.SpawnEntity("Vaccinator", pos);
            foreach (var uid in new[] { diagnoser, vaccinator })
            {
                Assert.That(em.GetComponent<TransformComponent>(uid).Anchored, Is.True);
                em.GetComponent<ApcPowerReceiverComponent>(uid).Powered = true;
            }
            var lab = em.System<VirologyLabSystem>();
            var disease = em.System<VirologySystem>();
            var donor = em.SpawnEntity("MobHuman", pos);
            Assert.That(disease.Expose(donor, "Hematic", 100), Is.True);
            var sample = em.SpawnEntity("DiseaseSwab", pos);
            Assert.That(lab.Collect(sample, donor), Is.True);
            Assert.That(lab.Collect(sample, donor), Is.False);
            Assert.That(lab.Insert(diagnoser, sample), Is.True);
            Assert.That(lab.Start(diagnoser, VirologyCommand.Analyze), Is.True);
            Assert.That(lab.Start(diagnoser, VirologyCommand.Analyze), Is.False);
            var power = em.GetComponent<ApcPowerReceiverComponent>(diagnoser);
            power.Powered = false;
            lab.Advance(diagnoser, 60);
            Assert.That(em.GetComponent<VirologySampleComponent>(sample).Analyzed, Is.False);
            power.Powered = true;
            lab.Advance(diagnoser, 30);
            Assert.That(em.GetComponent<VirologySampleComponent>(sample).Analyzed, Is.True);
            Assert.That(lab.BuildState(diagnoser).Diseases.Keys, Does.Contain("Hematic"));
            var containers = em.System<SharedContainerSystem>();
            containers.Remove(sample, em.GetComponent<VirologyMachineComponent>(diagnoser).Sample);
            Assert.That(lab.Insert(vaccinator, sample), Is.True);
            lab.BuildState(vaccinator);
            Assert.That(lab.Start(vaccinator, VirologyCommand.Treat), Is.False, "Requires biomass.");
            var materials = em.System<SharedMaterialStorageSystem>();
            Assert.That(materials.TryChangeMaterialAmount(vaccinator, "Biomass", 10), Is.True);
            Assert.That(lab.Start(vaccinator, VirologyCommand.Treat), Is.True);
            Assert.That(lab.Start(vaccinator, VirologyCommand.Treat), Is.False);
            Assert.That(materials.GetMaterialAmount(vaccinator, "Biomass"), Is.EqualTo(5));
            lab.Advance(vaccinator, 60);
            var dose = em.AllEntities<VirologyDoseComponent>().Single(c => c.Comp.Disease == "Hematic").Owner;
            Assert.That(lab.Apply(dose, donor), Is.True);
            Assert.That(lab.Apply(dose, donor), Is.False);
            disease.Advance(donor, 120);
            var control = em.SpawnEntity("DiseaseSwab", pos);
            Assert.That(lab.Collect(control, donor), Is.True);
            Assert.That(em.GetComponent<VirologySampleComponent>(control).Diseases, Is.Empty);
            Assert.That(em.GetComponent<VirologySampleComponent>(sample).Diseases, Does.Contain("Hematic"), "Samples are snapshots.");
            Assert.That(lab.Start(vaccinator, VirologyCommand.Vaccinate), Is.True);
            lab.Advance(vaccinator, 60);
            Assert.That(materials.GetMaterialAmount(vaccinator, "Biomass"), Is.Zero);
            var vaccine = em.AllEntities<VirologyDoseComponent>().Single(c => c.Comp.Vaccine).Owner;
            var healthy = em.SpawnEntity("MobHuman", pos);
            Assert.That(lab.Apply(vaccine, healthy), Is.True);
            Assert.That(disease.Expose(healthy, "Hematic", 1000), Is.False);
            Assert.That(lab.Start(vaccinator, VirologyCommand.Treat), Is.False);
        });
    }
}

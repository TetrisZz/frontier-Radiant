using System.Linq;
using Content.Server._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Events;
using Content.Shared._Starlight.Medical.Limbs;
using Content.Shared.Alert;
using Content.Shared.Inventory;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.StepTrigger.Systems;
using Content.Shared.VendingMachines;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class EmbeddedGlassTest
{
    [Test]
    public async Task FootFragmentsCanBeRemovedAndVendorsSpawn()
    {
        // No graphical client: this tests authoritative anatomy and surgery events.
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        await server.WaitAssertion(() =>
        {
            var maps = entities.System<SharedMapSystem>();
            var map = maps.CreateMap();
            var coordinates = new EntityCoordinates(map, 0, 0);
            var patient = entities.SpawnEntity("MobHuman", coordinates);
            var anatomy = entities.System<SharedBodySystem>();
            var feet = anatomy.GetBodyChildren(patient).Where(part => part.Component.PartType == BodyPartType.Foot).ToArray();
            Assert.That(feet.Length, Is.EqualTo(2));

            var shard = entities.SpawnEntity("ShardGlass", coordinates);
            var step = new StepTriggeredOffEvent(shard, patient);
            entities.EventBus.RaiseLocalEvent(shard, ref step);
            var injured = feet.Single(part => entities.TryGetComponent<EmbeddedGlassComponent>(part.Id, out var glass)
                && glass.Fragments.Count == 1);
            Assert.That(entities.GetComponent<EmbeddedGlassComponent>(injured.Id).Fragments[0].Id, Is.EqualTo("ShardGlass"));
            Assert.That(entities.GetComponent<AlertsComponent>(patient).Alerts.Keys.Any(key => key.AlertType?.Id == "EmbeddedGlass"), Is.True);

            var operationStep = entities.SpawnEntity("SurgeryStepExtractEmbeddedGlass", coordinates);
            var completed = new SurgeryStepCompleteEvent(patient, patient, injured.Id, new())
            {
                StepProto = "SurgeryStepExtractEmbeddedGlass",
                SurgeryProto = "SurgeryExtractEmbeddedGlass",
                IsFinal = true,
            };
            entities.EventBus.RaiseLocalEvent(operationStep, ref completed);
            Assert.That(entities.GetComponent<EmbeddedGlassComponent>(injured.Id).Fragments, Is.Empty);
            Assert.That(entities.GetComponent<AlertsComponent>(patient).Alerts.Keys.Any(key => key.AlertType?.Id == "EmbeddedGlass"), Is.False);

            var protectedPatient = entities.SpawnEntity("MobHuman", coordinates);
            var shoes = entities.SpawnEntity("ClothingShoesColorBlack", coordinates);
            Assert.That(entities.System<InventorySystem>().TryEquip(protectedPatient, shoes, "shoes", force: true), Is.True);
            var protectedShard = entities.SpawnEntity("ShardGlass", coordinates);
            var protectedStep = new StepTriggeredOffEvent(protectedShard, protectedPatient);
            entities.EventBus.RaiseLocalEvent(protectedShard, ref protectedStep);
            Assert.That(anatomy.GetBodyChildren(protectedPatient).Any(part => entities.HasComponent<EmbeddedGlassComponent>(part.Id)), Is.False);

            var cyberPatient = entities.SpawnEntity("MobHuman", coordinates);
            foreach (var foot in anatomy.GetBodyChildren(cyberPatient).Where(part => part.Component.PartType == BodyPartType.Foot))
                entities.AddComponent<ReversibleCyberLimbComponent>(foot.Id);
            var cyberShard = entities.SpawnEntity("ShardGlass", coordinates);
            var cyberStep = new StepTriggeredOffEvent(cyberShard, cyberPatient);
            entities.EventBus.RaiseLocalEvent(cyberShard, ref cyberStep);
            Assert.That(anatomy.GetBodyChildren(cyberPatient).Any(part => entities.HasComponent<EmbeddedGlassComponent>(part.Id)), Is.False);

            var vendor = entities.SpawnEntity("VendingMachineRadiantWater", coordinates);
            Assert.That(entities.GetComponent<VendingMachineComponent>(vendor).PackPrototypeId, Is.EqualTo("BodaInventory"));
            foreach (var prototype in new[] { "VendingMachineRadiantExpedition", "SmartFridgeRadiant", "SurgicalSterilizer", "SurgicalSterileDrape" })
                entities.SpawnEntity(prototype, coordinates);
            entities.System<EmbeddedGlassSystem>().Update(1f);

            var treatment = entities.SpawnEntity("SurgeryStepDrape", coordinates);
            var drape = entities.SpawnEntity("SurgicalSterileDrape", coordinates);
            var drapeState = entities.GetComponent<SurgicalItemSterilityComponent>(drape);
            drapeState.Dirty = true;
            var check = new SurgeryCanPerformStepEvent(patient, patient, new() { drape }, SlotFlags.NONE);
            entities.EventBus.RaiseLocalEvent(treatment, ref check);
            Assert.That(check.Invalid, Is.EqualTo(StepInvalidReason.DirtyDrape));
            Assert.That(check.Popup, Is.Not.Null.And.Not.Empty);

            var sterilizer = entities.SpawnEntity("SurgicalSterilizer", coordinates);
            Assert.That(entities.GetComponent<SurgicalSterilizerComponent>(sterilizer).CycleSeconds, Is.EqualTo(10));

            var tissuePatient = entities.SpawnEntity("MobHuman", coordinates);
            var torso = anatomy.GetBodyChildren(tissuePatient).Single(part => part.Component.PartType == BodyPartType.Torso).Id;
            var head = anatomy.GetBodyChildren(tissuePatient).Single(part => part.Component.PartType == BodyPartType.Head).Id;
            var hand = anatomy.GetBodyChildren(tissuePatient).First(part => part.Component.PartType == BodyPartType.Hand).Id;
            var heart = anatomy.GetPartOrgans(torso).Single(organ => entities.HasComponent<OrganHeartComponent>(organ.Id)).Id;
            var eyes = anatomy.GetPartOrgans(head).Single(organ => entities.HasComponent<OrganEyesComponent>(organ.Id)).Id;
            var surgery = entities.System<SurgerySystem>();
            var heartImplant = entities.SpawnEntity("ChestImplantHemostatic", coordinates);
            surgery.ContaminateImplantOrgan(heartImplant, torso, 5);
            Assert.That(entities.HasComponent<SurgicalOrganNecrosisComponent>(heart), Is.False, "Container residue alone must not immediately necrose tissue.");
            entities.GetComponent<SurgicalItemSterilityComponent>(heartImplant).Dirty = true;
            surgery.ContaminateImplantOrgan(heartImplant, torso, 15);
            Assert.That(entities.HasComponent<SurgicalOrganNecrosisComponent>(heart), Is.True);
            Assert.That(entities.HasComponent<SurgicalOrganNecrosisComponent>(eyes), Is.False);
            var eyeImplant = entities.SpawnEntity("EyeImplantMedical", coordinates);
            entities.GetComponent<SurgicalItemSterilityComponent>(eyeImplant).Dirty = true;
            surgery.ContaminateImplantOrgan(eyeImplant, head, 15);
            Assert.That(entities.HasComponent<SurgicalOrganNecrosisComponent>(eyes), Is.True);
            var handImplant = entities.SpawnEntity("HandImplantInsulated", coordinates);
            entities.GetComponent<SurgicalItemSterilityComponent>(handImplant).Dirty = true;
            surgery.ContaminateImplantOrgan(handImplant, hand, 15);
            Assert.That(entities.GetComponent<SurgicalSterilityComponent>(hand).NecrosisSeconds.ContainsKey(SurgicalSite.Surface), Is.True);
        });
    }
}

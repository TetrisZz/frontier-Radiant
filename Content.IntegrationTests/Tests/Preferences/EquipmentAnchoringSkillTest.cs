using System.Numerics;
using Content.Server.Construction.Components;
using Content.Server.Hands.Systems;
using Content.Server.Kitchen.Components;
using Content.Server.Power.Components;
using Content.Shared._radiant.Skills;
using Content.Shared.Construction.Components;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Wires;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Preferences;

public sealed class EquipmentAnchoringSkillTest
{
    [Test]
    public async Task ElectricRangeCanBeServicedWithEngineeringAndNoCookingSkill()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid user = default, range = default, rped = default, capacitor = default;
        await pair.Server.WaitAssertion(() =>
        {
            var userCoords = map.GridCoords.Offset(new Vector2(1, 0));
            user = em.SpawnEntity("MobHuman", userCoords);
            range = em.SpawnEntity("KitchenElectricRange", map.GridCoords);
            rped = em.SpawnEntity("RPED", userCoords);
            capacitor = em.SpawnEntity("SuperCapacitorStockPart", userCoords);
            Assert.That(em.System<SharedStorageSystem>().Insert(rped, capacitor, out _, playSound: false), Is.True);
            Assert.That(em.System<HandsSystem>().TryPickupAnyHand(user, rped), Is.True);
            var ranks = em.EnsureComponent<ProfessionalSkillsComponent>(user);
            var interaction = em.System<SharedInteractionSystem>();
            Assert.That(interaction.InteractUsing(user, rped, range, map.GridCoords), Is.False,
                "The RPED requires Engineering 1.");
            ranks.Levels[(int) ProfessionalSkill.Engineering] = 1;

            em.EnsureComponent<WiresPanelComponent>(range);
            em.GetComponent<ApcPowerReceiverComponent>(range).Powered = true;
            Assert.That(interaction.InteractUsing(user, rped, range, map.GridCoords), Is.True);
        });
        await pair.Server.WaitRunTicks(240);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<MachineComponent>(range).PartContainer.Contains(capacitor), Is.False,
                "A closed maintenance panel must still block upgrades.");
            var screwdriver = em.SpawnEntity("Screwdriver", em.GetComponent<TransformComponent>(user).Coordinates);
            Assert.That(em.System<HandsSystem>().TryPickupAnyHand(user, screwdriver), Is.True);
            Assert.That(em.System<SharedInteractionSystem>().InteractUsing(user, screwdriver, range, map.GridCoords), Is.True);
        });
        await pair.Server.WaitRunTicks(120);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<WiresPanelComponent>(range).Open, Is.True);
            em.GetComponent<ApcPowerReceiverComponent>(range).Powered = true;
            Assert.That(em.System<SharedInteractionSystem>().InteractUsing(user, rped, range, map.GridCoords), Is.True);
        });
        await pair.Server.WaitRunTicks(240);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<MachineComponent>(range).PartContainer.Contains(capacitor), Is.True);
            var dough = em.SpawnEntity("FoodDoughSlice", map.GridCoords);
            em.GetComponent<ApcPowerReceiverComponent>(range).Powered = true;
            Assert.That(em.System<SharedInteractionSystem>().InteractUsing(user, dough, range, map.GridCoords), Is.False,
                "Loading ingredients must still require Cooking.");
            Assert.That(em.GetComponent<MicrowaveComponent>(range).Storage.ContainedEntities, Is.Empty,
                "The stove must not take a tool or its parts as ingredients.");
        });
        Assert.That(pair.ServerLogHandler.FailingLogs, Is.Empty);
        await pair.CleanReturnAsync();
    }

    [TestCase("KitchenElectricRange", 1)]
    [TestCase("SeedExtractor", 1)]
    [TestCase("VendingMachineSeeds", 1)]
    [TestCase("KitchenElectricRange", 0)]
    [TestCase("SeedExtractor", 0)]
    public async Task WrenchingUsesEngineeringInsteadOfTheMachinesProfession(string prototype, int engineering)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;
        EntityUid user = default;
        EntityUid equipment = default;
        EntityUid wrench = default;
        await pair.Server.WaitAssertion(() =>
        {
            user = em.SpawnEntity("MobHuman", map.GridCoords.Offset(new Vector2(1, 0)));
            var ranks = em.EnsureComponent<ProfessionalSkillsComponent>(user);
            ranks.Levels[(int) ProfessionalSkill.Engineering] = engineering;
            equipment = em.SpawnEntity(prototype, map.GridCoords);
            if (em.HasComponent<LockComponent>(equipment))
                em.System<LockSystem>().Unlock(equipment, null);
            Assert.That(em.GetComponent<TransformComponent>(equipment).Anchored, Is.True);
            wrench = em.SpawnEntity("Wrench", map.GridCoords);
            Assert.That(em.System<HandsSystem>().TryPickupAnyHand(user, wrench), Is.True);
            var skills = em.System<SharedProfessionalSkillsSystem>();
            if (prototype != "VendingMachineSeeds")
                Assert.That(skills.CanUse(user, equipment, SkillAction.Interact, false), Is.False,
                    "Operating the machine must still require Cooking/Botany.");
            var food = em.SpawnEntity("FoodApple", map.GridCoords);
            if (prototype != "VendingMachineSeeds")
                Assert.That(skills.CanInteractUsing(user, food, equipment), Is.False,
                    "Inserting produce must not gain the wrench exception.");
            Assert.That(em.System<SharedInteractionSystem>().InteractUsing(user, wrench, equipment, map.GridCoords), Is.True);
        });
        await pair.Server.WaitRunTicks(600);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(equipment).Anchored, Is.False,
                "The real wrench interaction must finish with Cooking 0 and Botany 0.");
            Assert.That(em.System<SharedInteractionSystem>().InteractUsing(user, wrench, equipment, map.GridCoords), Is.True);
        });
        await pair.Server.WaitRunTicks(600);
        await pair.Server.WaitAssertion(() =>
        {
            Assert.That(em.GetComponent<TransformComponent>(equipment).Anchored, Is.True);
            em.DeleteEntity(equipment);

            // An appropriate tool must not bypass explicit advanced installation requirements.
            var advanced = em.SpawnEntity("TeslaGenerator", map.GridCoords);
            var skills = em.System<SharedProfessionalSkillsSystem>();
            Assert.That(em.HasComponent<AnchorableComponent>(advanced), Is.True);
            Assert.That(skills.CanInteractUsing(user, wrench, advanced), Is.False);
            em.GetComponent<ProfessionalSkillsComponent>(user).Levels[(int) ProfessionalSkill.Engineering] = 2;
            Assert.That(skills.CanInteractUsing(user, wrench, advanced), Is.True);
            em.DeleteEntity(advanced);
        });
        Assert.That(pair.ServerLogHandler.FailingLogs, Is.Empty);
        await pair.CleanReturnAsync();
    }
}

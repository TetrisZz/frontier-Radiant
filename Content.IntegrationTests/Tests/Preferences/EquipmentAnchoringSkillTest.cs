using System.Numerics;
using Content.Server.Hands.Systems;
using Content.Shared._radiant.Skills;
using Content.Shared.Construction.Components;
using Content.Shared.Interaction;
using Content.Shared.Lock;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Preferences;

public sealed class EquipmentAnchoringSkillTest
{
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

using Content.Server.Kitchen.Components;
using Content.Server.Kitchen.EntitySystems;
using Content.Server.Power.Components;
using Content.Shared._radiant.Skills;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Preferences;

public sealed class MicrowaveCookingSkillTest
{
    [Test]
    public async Task ComplexMicrowaveRecipeUnlocksAtOneAndDonkpocketsStaySkillFree()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                var skills = em.EnsureComponent<ProfessionalSkillsComponent>(user);
                var microwave = em.SpawnEntity("KitchenMicrowave", map.GridCoords);
                em.GetComponent<ApcPowerReceiverComponent>(microwave).Powered = true;
                var cooker = em.GetComponent<MicrowaveComponent>(microwave);
                cooker.CurrentCookTimerTime = 15;
                var bowl = em.SpawnEntity("FoodBowlBig", map.GridCoords);
                var beaker = em.SpawnEntity("Beaker", map.GridCoords);
                Assert.That(em.System<SharedSolutionContainerSystem>().TryGetSolution(beaker, "beaker", out _, out var solution), Is.True);
                solution!.AddReagent("Rice", 15);
                solution.AddReagent("Milk", 10);
                solution.AddReagent("Sugar", 5);
                var containers = em.System<SharedContainerSystem>();
                Assert.That(containers.Insert(bowl, cooker.Storage), Is.True);
                Assert.That(containers.Insert(beaker, cooker.Storage), Is.True);
                var cooking = em.System<MicrowaveSystem>();
                skills.Levels[(int) ProfessionalSkill.Cooking] = 0;
                cooking.Wzhzhzh(microwave, cooker, user);
                Assert.That(em.HasComponent<ActiveMicrowaveComponent>(microwave), Is.False);
                skills.Levels[(int) ProfessionalSkill.Cooking] = 1;
                cooking.Wzhzhzh(microwave, cooker, user);
                Assert.That(em.TryGetComponent<ActiveMicrowaveComponent>(microwave, out var active), Is.True);
                Assert.That(active!.PortionedRecipe.Item1?.ID, Is.EqualTo("RecipeRicePudding"),
                    "Cooking 1 must start the actual complex recipe, not just an unmatched heating cycle.");
                Assert.That(active.PortionedRecipe.Item1!.RequiredCookingLevel, Is.EqualTo(2),
                    "The appliance override must not lower recipe requirements for other cooking appliances.");
                containers.EmptyContainer(cooker.Storage, force: true);
                em.DeleteEntity(microwave);

                microwave = em.SpawnEntity("KitchenMicrowave", map.GridCoords);
                em.GetComponent<ApcPowerReceiverComponent>(microwave).Powered = true;
                cooker = em.GetComponent<MicrowaveComponent>(microwave);
                cooker.CurrentCookTimerTime = 5;
                var pocket = em.SpawnEntity("FoodDonkpocket", map.GridCoords);
                Assert.That(containers.Insert(pocket, cooker.Storage), Is.True);
                skills.Levels[(int) ProfessionalSkill.Cooking] = 0;
                cooking.Wzhzhzh(microwave, cooker, user);
                Assert.That(em.GetComponent<ActiveMicrowaveComponent>(microwave).PortionedRecipe.Item1?.ID,
                    Is.EqualTo("RecipeWarmDonkpocket"));
                containers.EmptyContainer(cooker.Storage, force: true);
                em.DeleteEntity(microwave);
            });
            Assert.That(pair.ServerLogHandler.FailingLogs, Is.Empty);
            await pair.CleanReturnAsync();
        }
        catch (System.Exception exception)
        {
            TestContext.Out.WriteLine(exception);
            throw;
        }
    }
}

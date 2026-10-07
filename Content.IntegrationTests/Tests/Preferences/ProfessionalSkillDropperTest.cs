using Content.Server.Chemistry.EntitySystems;
using Content.Shared._radiant.Skills;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Interaction;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Preferences;

[TestFixture]
public sealed class ProfessionalSkillDropperTest
{
    [Test]
    public async Task DropperChecksBotanyBeforeConsumingReagents()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        await server.WaitAssertion(() =>
        {
            var entities = server.ResolveDependency<IEntityManager>();
            var solutions = entities.System<SharedSolutionContainerSystem>();
            var injectors = entities.System<InjectorSystem>();
            var user = entities.SpawnEntity(null, MapCoordinates.Nullspace);
            var skills = entities.AddComponent<ProfessionalSkillsComponent>(user);
            skills.Levels[(int) ProfessionalSkill.Medicine] = 1;
            var tray = entities.SpawnEntity("HydroponicsTrayEmpty", MapCoordinates.Nullspace);
            Assert.That(solutions.TryGetRefillableSolution(tray, out var destination, out var soil), Is.True);
            var dropper = entities.SpawnEntity("Dropper", MapCoordinates.Nullspace);
            var injector = entities.GetComponent<InjectorComponent>(dropper);
            Assert.That(solutions.TryGetSolution(dropper, injector.SolutionName, out var source, out var liquid), Is.True);
            var cases = new[] { ("UnstableMutagen", 3), ("Left4Zed", 3), ("EZNutrient", 1), ("Water", 0) };
            foreach (var (reagent, required) in cases)
            {
                for (var level = 0; level <= 3; level++)
                {
                    solutions.RemoveAllSolution(source!.Value);
                    solutions.RemoveAllSolution(destination!.Value);
                    solutions.TryAddSolution(source.Value, new Solution(reagent, 5));
                    skills.Levels[(int) ProfessionalSkill.Botany] = level;
                    injectors.SetMode((dropper, injector), InjectorToggleMode.Inject);
                    var interaction = new AfterInteractEvent(user, dropper, tray,
                        entities.GetComponent<TransformComponent>(tray).Coordinates, true);
                    entities.EventBus.RaiseLocalEvent(dropper, interaction);
                    var transferred = level >= required;
                    Assert.That(liquid!.Volume.Float(), Is.EqualTo(transferred ? 4f : 5f),
                        $"{reagent}: source at botany {level}");
                    Assert.That(soil!.GetTotalPrototypeQuantity(reagent).Float(), Is.EqualTo(transferred ? 1f : 0f),
                        $"{reagent}: destination at botany {level}");
                }
            }
            entities.DeleteEntity(dropper);
            entities.DeleteEntity(tray);
            entities.DeleteEntity(user);
        });
    }
}

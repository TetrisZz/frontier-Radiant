using System.Collections.Generic;
using Content.Shared._Starlight.Medical.Limbs;
using Content.Shared.Chemistry.EntitySystems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class AngelInjectorRegenerationTest
{
    [Test]
    public async Task BothHandsRegenerateCorrectReagentsAfterEmptying()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        var injectors = new List<(EntityUid Entity, string Reagent)>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IEntityManager>().System<SharedMapSystem>().CreateMap();
            foreach (var handId in new[] { "LeftHandCyberAngel", "RightHandCyberAngel" })
            {
                var hand = entities.SpawnEntity(handId, new EntityCoordinates(map, 0, 0));
                foreach (var item in entities.GetComponent<LimbItemStorageComponent>(hand).ItemEntities)
                {
                    var id = entities.GetComponent<MetaDataComponent>(item).EntityPrototype?.ID;
                    if (id == "AutoinjectorCyberTricordrazine")
                        injectors.Add((item, "Tricordrazine"));
                    else if (id == "AutoinjectorCyberInaprovaline")
                        injectors.Add((item, "Inaprovaline"));
                }
            }
            Assert.That(injectors.Count, Is.EqualTo(4));
        });
        for (var cycle = 0; cycle < 2; cycle++)
        {
            await server.WaitRunTicks(2400);
            await server.WaitAssertion(() =>
            {
                var solutions = entities.System<SharedSolutionContainerSystem>();
                foreach (var (uid, reagent) in injectors)
                {
                    Assert.That(solutions.TryGetSolution(uid, "hypospray", out var solution), Is.True);
                    var contents = solution!.Value.Comp.Solution;
                    Assert.That(contents.Volume.Float(), Is.EqualTo(15), $"Cycle {cycle}, {reagent}");
                    Assert.That(contents.GetTotalPrototypeQuantity(reagent).Float(), Is.EqualTo(15));
                    solutions.RemoveAllSolution(solution.Value);
                    Assert.That(contents.Volume.Float(), Is.Zero);
                }
            });
        }
    }
}

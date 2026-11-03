using Content.Server._radiant.ResearchBlueprints;
using Content.Server.Power.Components;
using Content.Server.Research.Systems;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using Content.Shared.Research.Systems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests;

public sealed class BlueprintLibrarySelectionTest
{
    [Test]
    public async Task SelectionsSurviveDisconnectionButNotEjectionUnselectionOrReplacement()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var research = em.System<ResearchSystem>();
                var blueprints = em.System<BlueprintResearchSystem>();
                var transform = em.System<SharedTransformSystem>();
                var containers = em.System<SharedContainerSystem>();
                const string recipe = "ClothingBackpackHolding";
                EntityUid Spawn(string prototype)
                {
                    var entity = em.SpawnEntity(prototype, map.GridCoords);
                    if (em.TryGetComponent<ApcPowerReceiverComponent>(entity, out var power))
                    {
                        power.NeedsPower = false;
                        power.Powered = true;
                    }
                    return entity;
                }
                var server = Spawn("ResearchAndDevelopmentServer");
                var library = Spawn("BlueprintLithograph");
                var otherLibrary = Spawn("BlueprintLithograph");
                var lathe = Spawn("EngineeringTechFab");
                void Connect(EntityUid client)
                {
                    research.UnregisterClient(client);
                    research.RegisterClient(client, server);
                    Assert.That(em.GetComponent<ResearchClientComponent>(client).Server, Is.EqualTo(server));
                }
                foreach (var client in new[] { library, otherLibrary, lathe })
                    Connect(client);
                var selections = em.GetComponent<BlueprintLibraryComponent>(library).SelectedRecipes;
                var recipes = em.GetComponent<LatheComponent>(lathe).BlueprintRecipes;
                BlueprintLibraryState State()
                {
                    Assert.That(em.System<SharedUserInterfaceSystem>().TryGetUiState<BlueprintLibraryState>(
                        library, BlueprintLibraryUiKey.Key, out var state), Is.True);
                    return state!;
                }
                EntityUid Insert(EntityUid machine, string instance, int multiplier)
                {
                    var entity = Spawn("ResearchRandomBlueprint");
                    var data = em.GetComponent<BlueprintComponent>(entity);
                    blueprints.SetBlueprint((entity, data), [recipe], new BlueprintModifiers
                    {
                        InstanceId = instance, Lathe = "EngineeringTechFab",
                        MaterialMultiplier = multiplier, TimeMultiplier = multiplier,
                    });
                    Assert.That(em.System<BlueprintSystem>().TryInsertBlueprint(
                        (machine, em.GetComponent<BlueprintReceiverComponent>(machine)), (entity, data), null), Is.True);
                    return entity;
                }
                var first = Insert(library, "retained-first", 2);
                var replacement = Insert(otherLibrary, "retained-replacement", 3);
                Assert.That(blueprints.TrySelectBlueprint(library, first, recipe), Is.True);

                var serverPower = em.GetComponent<ApcPowerReceiverComponent>(server);
                serverPower.NeedsPower = true;
                serverPower.Powered = false;
                blueprints.UpdateLibrary(library);
                Assert.That(State().Connected, Is.False);
                Assert.That(State().Selected[recipe], Is.EqualTo("retained-first"));
                serverPower.NeedsPower = false;
                serverPower.Powered = true;
                blueprints.UpdateLibrary(library);
                Assert.That(State().Connected, Is.True);
                Assert.That(State().Selected[recipe], Is.EqualTo("retained-first"));

                transform.Unanchor(library);
                Assert.That(em.GetComponent<ResearchClientComponent>(library).Server, Is.Null);
                Assert.That(recipes.ContainsKey(recipe), Is.False, "Disconnected lithographs must withdraw recipes from lathes.");
                Assert.That(selections[recipe], Is.EqualTo("retained-first"));
                Assert.That(State().Selected[recipe], Is.EqualTo("retained-first"));
                transform.AnchorEntity(library, em.GetComponent<TransformComponent>(library));
                Assert.That(em.GetComponent<ResearchClientComponent>(library).Server, Is.EqualTo(server));
                Assert.That(recipes[recipe].InstanceId, Is.EqualTo("retained-first"));
                Assert.That(recipes[recipe].MaterialMultiplier, Is.EqualTo(2));

                transform.Unanchor(server);
                Assert.That(em.GetComponent<ResearchClientComponent>(library).Server, Is.Null);
                Assert.That(selections[recipe], Is.EqualTo("retained-first"));
                transform.AnchorEntity(server, em.GetComponent<TransformComponent>(server));
                Connect(lathe);
                Connect(library);
                Assert.That(recipes[recipe].InstanceId, Is.EqualTo("retained-first"));

                Assert.That(blueprints.TryUnselectBlueprint(library, first, recipe), Is.True);
                Connect(library);
                Assert.That(selections, Is.Empty, "Manual unselection must survive reconnection.");
                Assert.That(recipes.ContainsKey(recipe), Is.False);

                Assert.That(blueprints.TrySelectBlueprint(library, first, recipe), Is.True);
                research.UnregisterClient(library);
                Assert.That(containers.Remove(first, containers.GetContainer(library, "blueprint")), Is.True);
                Assert.That(selections, Is.Empty, "Ejection while disconnected must forget the selection too.");
                Connect(library);
                Assert.That(recipes.ContainsKey(recipe), Is.False);

                Assert.That(em.System<BlueprintSystem>().TryInsertBlueprint(
                    (library, em.GetComponent<BlueprintReceiverComponent>(library)),
                    (first, em.GetComponent<BlueprintComponent>(first)), null), Is.True);
                Assert.That(blueprints.TrySelectBlueprint(library, first, recipe), Is.True);
                research.UnregisterClient(library);
                Connect(otherLibrary);
                Assert.That(blueprints.TrySelectBlueprint(otherLibrary, replacement, recipe), Is.True);
                Connect(library);
                Assert.That(recipes[recipe].InstanceId, Is.EqualTo("retained-replacement"));
                Assert.That(selections, Is.Empty, "Reconnection must not overwrite a newer choice from another lithograph.");
            });
            await pair.ReallyBeIdle();
            await pair.CleanReturnAsync();
        }
        catch (System.Exception exception)
        {
            TestContext.Out.WriteLine(exception);
            throw;
        }
    }
}

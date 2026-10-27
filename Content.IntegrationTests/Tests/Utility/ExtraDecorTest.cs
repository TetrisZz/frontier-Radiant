using System;
using System.Linq;
using System.Numerics;
using Content.Client._radiant.Decorations;
using Content.Server._radiant.Decorations;
using Content.Shared._radiant.Decorations;
using Content.Shared._radiant.Fishing.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Construction.Prototypes;
using Content.Shared.FixedPoint;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class ExtraDecorTest
{
    [Test]
    public async Task NewAquariumsAcceptWaterFoodFishAndDecorationsAndHaveConstructionRecipes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var prototypes = pair.Server.ProtoMan;
                Assert.That(prototypes.HasIndex<EntityPrototype>("FishSilver"), Is.False);
                foreach (var id in new[] { "RadiantSmallStairs", "RadiantAquariumTech", "RadiantAquariumCompact" })
                {
                    var recipe = prototypes.Index<ConstructionPrototype>(id);
                    var graph = prototypes.Index(recipe.Graph);
                    Assert.That(recipe.Hide, Is.False, id);
                    Assert.That(graph.TryPath(recipe.StartNode, recipe.TargetNode, out _), Is.True, id);
                    Assert.That(graph.TryPath(recipe.TargetNode, recipe.StartNode, out _), Is.True, id);
                    Assert.That(graph.Nodes[recipe.TargetNode].Entity, Is.Not.Null, id);
                }

                var hands = em.System<SharedHandsSystem>();
                var interaction = em.System<SharedInteractionSystem>();
                var solutions = em.System<SharedSolutionContainerSystem>();
                var storage = em.System<SharedStorageSystem>();
                var user = em.SpawnEntity("MobHumanDummy", map.GridCoords.Offset(new Vector2(1, 0)));
                hands.AddHand(user, "decor-test", HandLocation.Right);
                hands.SetActiveHand(user, "decor-test");
                foreach (var (id, volume, capacity) in new[]
                {
                    ("RadiantAquariumTech", 200, 4), ("RadiantAquariumCompact", 100, 2),
                })
                {
                    var filled = em.SpawnEntity(id + "Filled", map.GridCoords);
                    Assert.That(em.System<AquariumSystem>().HasWater(filled), Is.True);
                    if (id == "RadiantAquariumCompact")
                    {
                        Assert.That(em.GetComponent<TransformComponent>(filled).Anchored, Is.False);
                        Assert.That(hands.TryPickup(user, filled, "decor-test", animate: false), Is.True);
                        Assert.That(hands.TryDrop(user, filled), Is.True);
                    }
                    em.DeleteEntity(filled);
                    var tank = em.SpawnEntity(id, map.GridCoords);
                    var aquarium = em.GetComponent<AquariumComponent>(tank);
                    Assert.That(aquarium.Capacity, Is.EqualTo(capacity));
                    Assert.That(em.HasComponent<AquariumBreedingComponent>(tank), Is.True);
                    Assert.That(solutions.TryGetSolution(tank, "tank", out _, out var water), Is.True);
                    Assert.That(water!.Volume, Is.EqualTo(FixedPoint2.Zero));
                    var bucket = em.SpawnEntity("Bucket", map.GridCoords);
                    Assert.That(solutions.TryGetSolution(bucket, "drink", out var bucketSolution, out _), Is.True);
                    solutions.TryAddReagent(bucketSolution!.Value, "Water", FixedPoint2.New(250));
                    Assert.That(hands.TryPickup(user, bucket, "decor-test", checkActionBlocker: false, animate: false), Is.True);
                    for (var i = 0; i < 2; i++)
                        Assert.That(interaction.InteractUsing(user, bucket, tank, map.GridCoords), Is.True);
                    Assert.That(water.Volume, Is.EqualTo(FixedPoint2.New(volume)), id);
                    hands.TryDrop(user, bucket);
                    em.DeleteEntity(bucket);
                    aquarium.Food = 0;
                    var food = em.SpawnEntity("RadiantAquariumFeed", map.GridCoords);
                    Assert.That(hands.TryPickup(user, food, "decor-test", checkActionBlocker: false, animate: false), Is.True);
                    Assert.That(interaction.InteractUsing(user, food, tank, map.GridCoords), Is.True);
                    Assert.That(aquarium.Food, Is.EqualTo(10));
                    Assert.That(em.GetComponent<AquariumFeedComponent>(food).Uses, Is.EqualTo(19));
                    hands.TryDrop(user, food);
                    em.DeleteEntity(food);
                    foreach (var decoration in new[] { "RadiantAquariumSand", "RadiantAquariumRocks", "RadiantAquariumSeaweed", "RadiantAquariumTreasure" })
                        Assert.That(storage.Insert(tank, em.SpawnEntity(decoration, map.GridCoords), out _, playSound: false), Is.True);
                    for (var i = 0; i < capacity; i++)
                    {
                        var fish = em.SpawnEntity("FishBass", map.GridCoords);
                        Assert.That(storage.Insert(tank, fish, out _, playSound: false), Is.True);
                        Assert.That(em.GetComponent<FishComponent>(fish).InAquariumWater, Is.True);
                    }
                    var extra = em.SpawnEntity("FishBass", map.GridCoords);
                    Assert.That(storage.Insert(tank, extra, out _, playSound: false), Is.False, "Respect aquarium capacity.");
                    em.DeleteEntity(extra);
                    if (id == "RadiantAquariumCompact")
                    {
                        Assert.That(em.HasComponent<ItemComponent>(tank), Is.True);
                        Assert.That(em.GetComponent<TransformComponent>(tank).Anchored, Is.False);
                        var occupants = em.GetComponent<StorageComponent>(tank).Container.ContainedEntities.ToArray();
                        var pickup = new InteractHandEvent(user, tank);
                        em.EventBus.RaiseLocalEvent(tank, pickup);
                        Assert.That(pickup.Handled, Is.True);
                        Assert.That(hands.IsHolding(user, tank), Is.True, "Pick up the populated tank with an empty hand.");
                        Assert.That(water.Volume, Is.EqualTo(FixedPoint2.New(volume)));
                        Assert.That(aquarium.Food, Is.EqualTo(10));
                        Assert.That(em.GetComponent<StorageComponent>(tank).Container.ContainedEntities, Is.EquivalentTo(occupants));
                        foreach (var fish in occupants.Where(em.HasComponent<FishComponent>))
                            Assert.That(em.GetComponent<FishComponent>(fish).InAquariumWater, Is.True);
                        Assert.That(hands.TryDrop(user, tank), Is.True);
                        Assert.That(water.Volume, Is.EqualTo(FixedPoint2.New(volume)));
                        Assert.That(em.GetComponent<StorageComponent>(tank).Container.ContainedEntities, Is.EquivalentTo(occupants));
                        foreach (var fish in occupants.Where(em.HasComponent<FishComponent>))
                            Assert.That(em.GetComponent<FishComponent>(fish).InAquariumWater, Is.True);
                    }
                    else
                    {
                        Assert.That(em.HasComponent<ItemComponent>(tank), Is.False);
                        Assert.That(em.GetComponent<TransformComponent>(tank).Anchored, Is.True);
                    }
                    foreach (var item in em.GetComponent<StorageComponent>(tank).Container.ContainedEntities.ToArray())
                        em.DeleteEntity(item);
                    em.DeleteEntity(tank);
                }
            });
            await pair.Client.WaitAssertion(() =>
            {
                var em = pair.Client.EntMan;
                var sprites = em.System<SpriteSystem>();
                var appearance = em.System<AppearanceSystem>();
                var localMap = em.System<SharedMapSystem>().CreateMap(out _);
                try
                {
                    foreach (var id in new[] { "RadiantAquarium", "RadiantAquariumTech", "RadiantAquariumCompact" })
                    {
                        var tank = em.SpawnEntity(id, new EntityCoordinates(localMap, 0, 0));
                        var sprite = em.GetComponent<SpriteComponent>(tank);
                        var aquarium = em.GetComponent<AquariumComponent>(tank);
                        foreach (var layer in sprite.AllLayers)
                            Assert.That(layer.ActualRsi!.TryGetState(layer.RsiState, out _), Is.True, id);
                        if (id == "RadiantAquariumCompact")
                        {
                            foreach (var hand in new[] { HandLocation.Left, HandLocation.Right })
                            {
                                var inhand = new GetInhandVisualsEvent(tank, hand);
                                em.EventBus.RaiseLocalEvent(tank, inhand);
                                Assert.That(inhand.Layers, Has.Count.EqualTo(1));
                                var visual = inhand.Layers.Single().Item2;
                                Assert.That(visual.RsiPath, Is.EqualTo("_radiant/Structures/Furniture/aquarium_compact.rsi"));
                                Assert.That(sprite.BaseRSI!.TryGetState(visual.State!, out _), Is.True);
                            }
                        }
                        appearance.SetData(tank, AquariumVisuals.Water, true);
                        appearance.SetData(tank, AquariumVisuals.Decorations, new[] { "sand", "rocks", "seaweeds_front", "treasure" });
                        appearance.SetData(tank, AquariumVisuals.Fish, new AquariumFishVisualState(
                            ["bass", "tropical"], [true, true], [Color.White, Color.White], [false, false]));
                        var changed = new AppearanceChangeEvent
                        {
                            Component = em.GetComponent<AppearanceComponent>(tank),
                            Sprite = sprite,
                        };
                        em.EventBus.RaiseLocalEvent(tank, ref changed);
                        em.System<WorkshopVisualsSystem>().Update(0);
                        foreach (var key in new[] { "water", "sand", "rocks", "seaweeds_front", "treasure", "fish0", "fish1" })
                        {
                            Assert.That(sprites.TryGetLayer((tank, sprite), key, out var layer, false), Is.True, id + ": " + key);
                            Assert.That(layer!.Visible, Is.True, id + ": " + key);
                        }
                        for (var i = 0; i < 2; i++)
                        {
                            sprites.TryGetLayer((tank, sprite), "fish" + i, out var layer, false);
                            Assert.That(Vector2.Distance(layer!.Offset, aquarium.FishOffsets[i]), Is.LessThan(0.04f), id);
                        }
                        em.DeleteEntity(tank);
                    }
                    var stairs = em.SpawnEntity("RadiantSmallStairs", new EntityCoordinates(localMap, 0, 0));
                    var stairSprite = em.GetComponent<SpriteComponent>(stairs);
                    Assert.That(stairSprite.BaseRSI!.Size, Is.EqualTo(new Vector2i(32, 48)));
                    Assert.That(stairSprite.BaseRSI.TryGetState("iron", out _), Is.True);
                    Assert.That(stairSprite.Offset, Is.EqualTo(new Vector2(0, 0.25f)));
                }
                finally
                {
                    em.DeleteEntity(localMap);
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}

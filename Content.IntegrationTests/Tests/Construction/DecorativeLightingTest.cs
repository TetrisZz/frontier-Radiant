using System;
using System.Linq;
using System.Numerics;
using Content.Client.Light.Visualizers;
using Content.Server.Construction.Components;
using Content.Server.GameTicking.Rules.Components;
using Content.Server.Power.Components;
using Content.Server.StationEvents.Components;
using Content.Server.StationEvents.Events;
using Content.Shared.Construction.Prototypes;
using Content.Shared.Construction.NodeEntities;
using Content.Shared.Construction.Steps;
using Content.Shared.GameTicking.Components;
using Content.Shared.Light.Components;
using Content.Shared.Light.EntitySystems;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Construction;

[TestFixture]
public sealed class DecorativeLightingTest
{
    private static readonly string[] Lamps =
    {
        "RadiantFloorLight", "RadiantLampPost", "RadiantLampPostFestive",
        "RadiantLampPostOne", "RadiantLampPostTwo", "RadiantLampPostThree",
        "RadiantLampPostFour", "RadiantLampPostSidewalk",
    };

    private static readonly string[] AdditionalLamps =
    {
        "RadiantLampPostBare", "RadiantLampPostChinese", "RadiantLampPostOneSnow", "RadiantLampPostTwoSnow",
        "RadiantLampPostThreeSnow", "RadiantLampPostFourSnow", "RadiantLampPostSidewalkSnow",
        "RadiantLampPostChineseSnow", "RadiantLampPostTraffic", "RadiantLampPostTrafficSnow",
    };

    [Test]
    public async Task SpawnedLampsHaveBulbsButConstructionAndUpgradesAreEmpty()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.EntMan;
                var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
                var flare = entities.SpawnEntity(null, map.GridCoords);
                entities.AddComponent<GameRuleComponent>(flare);
                entities.AddComponent<ActiveGameRuleComponent>(flare);
                // Make the flare deterministic so the ordinary lamp is a positive control.
#pragma warning disable RA0002
                entities.AddComponent<SolarFlareRuleComponent>(flare).LightBreakChancePerSecond = 1;
#pragma warning restore RA0002
                foreach (var id in Lamps)
                {
                    var lamp = entities.SpawnEntity(id, map.GridCoords);
                    var light = entities.GetComponent<PoweredLightComponent>(lamp);
                    var lighting = entities.System<SharedPoweredLightSystem>();
                    Assert.That(light.LightBulbContainer.ContainedEntity, Is.Not.Null, id);
                    Assert.That(light.HasLampOnSpawn, Is.Not.Null, id);
                    entities.DeleteEntity(lighting.EjectBulb(lamp)!.Value);
                    Assert.That(entities.HasComponent<ApcPowerReceiverComponent>(lamp), Is.True, id);
                    var construction = entities.GetComponent<ConstructionComponent>(lamp);
                    var recipe = prototypes.Index<ConstructionPrototype>(id);
                    var graph = prototypes.Index<ConstructionGraphPrototype>(recipe.Graph);
                    Assert.That(((StaticNodeEntity) graph.Nodes[recipe.TargetNode].Entity).Id, Is.EqualTo(id + "Empty"), id);
                    var built = entities.SpawnEntity(id + "Empty", map.GridCoords);
                    var builtLight = entities.GetComponent<PoweredLightComponent>(built);
                    Assert.That(builtLight.LightBulbContainer.ContainedEntity, Is.Null, id);
                    Assert.That(builtLight.HasLampOnSpawn, Is.Null, id);
                    entities.DeleteEntity(built);
                    Assert.That(construction.Node, Is.EqualTo(recipe.TargetNode), id);
                    Assert.That(graph.TryPath(recipe.StartNode, recipe.TargetNode, out var path), Is.True, id);
                    Assert.That(path, Is.Not.Empty, id);
                    Assert.That(graph.Edge(recipe.StartNode, recipe.TargetNode)!.Steps.Count, Is.GreaterThan(0), id);
                    Assert.That(graph.Edge(recipe.TargetNode, recipe.StartNode), Is.Not.Null, id);
                    var shieldedNode = recipe.TargetNode + "Shielded";
                    Assert.That(((StaticNodeEntity) graph.Nodes[shieldedNode].Entity).Id, Is.EqualTo(id + "ShieldedEmpty"), id);
                    var upgrade = graph.Edge(recipe.TargetNode, shieldedNode);
                    Assert.That(upgrade, Is.Not.Null, id);
                    var material = (MaterialConstructionGraphStep) upgrade!.Steps.Single();
                    Assert.That(material.MaterialPrototypeId.Id, Is.EqualTo("Plasteel"), id);
                    Assert.That(material.Amount, Is.EqualTo(id == "RadiantFloorLight" ? 1 : 2), id);
                    Assert.That(graph.Edge(shieldedNode, recipe.StartNode), Is.Not.Null, id);
                    var shielded = entities.SpawnEntity(id + "Shielded", map.GridCoords);
                    var shieldedLight = entities.GetComponent<PoweredLightComponent>(shielded);
                    Assert.That(shieldedLight.LightBulbContainer.ContainedEntity, Is.Not.Null, id);
                    entities.DeleteEntity(lighting.EjectBulb(shielded)!.Value);
                    var builtShielded = entities.SpawnEntity(id + "ShieldedEmpty", map.GridCoords);
                    Assert.That(entities.GetComponent<PoweredLightComponent>(builtShielded).LightBulbContainer.ContainedEntity, Is.Null, id);
                    Assert.That(entities.GetComponent<PoweredLightComponent>(builtShielded).SolarFlareShieldingCoefficient, Is.Zero, id);
                    entities.DeleteEntity(builtShielded);
                    Assert.That(shieldedLight.SolarFlareShieldingCoefficient, Is.Zero, id);
                    Assert.That(entities.GetComponent<ConstructionComponent>(shielded).Node, Is.EqualTo(shieldedNode), id);
                    var bulb = entities.SpawnEntity("LightBulb", map.GridCoords);
                    Assert.That(lighting.InsertBulb(shielded, bulb), Is.True, id);
                    Assert.That(shieldedLight.LightBulbContainer.ContainedEntity, Is.EqualTo(bulb), id);
                    var ordinaryBulb = entities.SpawnEntity("LightBulb", map.GridCoords);
                    Assert.That(lighting.InsertBulb(lamp, ordinaryBulb), Is.True, id);
                    entities.System<SolarFlareRule>().Update(2);
                    Assert.That(entities.GetComponent<LightBulbComponent>(ordinaryBulb).State, Is.EqualTo(LightBulbState.Broken), id);
                    Assert.That(entities.GetComponent<LightBulbComponent>(bulb).State, Is.EqualTo(LightBulbState.Normal), id);
                    Assert.That(lighting.EjectBulb(shielded), Is.EqualTo(bulb), id);
                    entities.DeleteEntity(bulb);
                    entities.DeleteEntity(shielded);
                    if (id == "RadiantFloorLight")
                        Assert.That(entities.GetComponent<PhysicsComponent>(lamp).CanCollide, Is.False);
                    entities.DeleteEntity(lamp);
                }
                foreach (var id in AdditionalLamps)
                {
                    var lamp = entities.SpawnEntity(id, map.GridCoords);
                    Assert.That(entities.HasComponent<ConstructionComponent>(lamp), Is.False, id);
                    Assert.That(prototypes.HasIndex<ConstructionPrototype>(id), Is.False, id);
                    if (entities.TryGetComponent<PoweredLightComponent>(lamp, out var light))
                        Assert.That(light.LightBulbContainer.ContainedEntity, Is.Not.Null, id);
                    entities.DeleteEntity(lamp);
                }
                entities.DeleteEntity(flare);
                TestContext.Out.WriteLine("Decorative lighting construction and bulb assertions completed");
            });
            await pair.Client.WaitAssertion(() =>
            {
                var entities = pair.Client.EntMan;
                var mapEntity = entities.System<SharedMapSystem>().CreateMap(out var mapId);
                foreach (var id in Lamps.Concat(Lamps.Select(id => id + "Shielded")).Concat(AdditionalLamps))
                {
                    var lamp = entities.SpawnEntity(id, new MapCoordinates(Vector2.Zero, mapId));
                    var sprite = entities.GetComponent<SpriteComponent>(lamp);
                    Assert.That(sprite.BaseRSI, Is.Not.Null, id);
                    var floor = id.StartsWith("RadiantFloorLight");
                    var small = id is "RadiantLampPost" or "RadiantLampPostShielded" or "RadiantLampPostFestive" or "RadiantLampPostFestiveShielded";
                    Assert.That(sprite.BaseRSI!.Size.X, Is.EqualTo(floor || small ? 32 : 96), id);
                    Assert.That(sprite.BaseRSI.Size.Y, Is.EqualTo(floor ? 32 : small ? 64 : 160), id);
                    Assert.That(sprite.Scale, Is.EqualTo(Vector2.One), id);
                    Assert.That(sprite.Offset, Is.EqualTo(new Vector2(0, floor ? 0 : small ? 0.5f : 2)), id);
                    Assert.That(sprite.NoRotation, Is.True, id);
                    if (entities.TryGetComponent<PoweredLightVisualsComponent>(lamp, out var visuals))
                    {
                        foreach (var state in visuals.SpriteStateMap.Values)
                            Assert.That(sprite.BaseRSI.TryGetState(state, out _), Is.True, $"{id}: {state}");
                    }
                    entities.DeleteEntity(lamp);
                }
                entities.DeleteEntity(mapEntity);
                TestContext.Out.WriteLine("Decorative lighting sprite and scale assertions completed");
            });
            await pair.CleanReturnAsync();
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Decorative lighting regression failure: {exception}");
            throw;
        }
    }
}

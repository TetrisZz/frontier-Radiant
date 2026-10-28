using System;
using System.Linq;
using System.Numerics;
using Content.Shared.Light.Components;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Construction;

public sealed class LampPostLightingTest
{
    [Test]
    public async Task EveryPoweredPostLightsItsOwnTileRegardlessOfRotation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            await pair.Client.WaitAssertion(() =>
            {
                var em = pair.Client.EntMan;
                var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
                var map = em.System<SharedMapSystem>().CreateMap(out var mapId);
                var transforms = em.System<SharedTransformSystem>();
                var posts = prototypes.EnumeratePrototypes<EntityPrototype>().Where(prototype =>
                    !prototype.Abstract && prototype.ID.StartsWith("RadiantLampPost", StringComparison.Ordinal)).ToArray();
                Assert.That(posts.Length, Is.GreaterThanOrEqualTo(30), "Include empty, shielded, and snow-covered variants.");
                var checkedLights = 0;
                foreach (var prototype in posts)
                {
                    var post = em.SpawnEntity(prototype.ID, new MapCoordinates(new Vector2(10, 10), mapId));
                    if (!em.HasComponent<PoweredLightComponent>(post))
                    {
                        em.DeleteEntity(post);
                        continue;
                    }

                    checkedLights++;
                    var light = em.GetComponent<PointLightComponent>(post);
                    var sprite = em.GetComponent<SpriteComponent>(post);
                    Assert.That(light.MaskPath, Is.Null, prototype.ID + ": a post must not use a directional light mask.");
                    Assert.That(light.CastShadows, Is.True, prototype.ID + ": do not fix misplaced lighting by shining through walls.");
                    var small = sprite.BaseRSI!.Size.Y == 64;
                    Assert.That(sprite.Offset, Is.EqualTo(new Vector2(0, small ? 0.5f : 2)), prototype.ID);
                    Assert.That(sprite.NoRotation, Is.True, prototype.ID);
                    foreach (var degrees in new[] { 0, 90, 180, 270 })
                    {
                        transforms.SetLocalRotation(post, Angle.FromDegrees(degrees));
                        Assert.That(light.Offset, Is.EqualTo(Vector2.Zero), prototype.ID);
                        var (position, rotation) = transforms.GetWorldPositionRotation(post);
                        var lightPosition = position + rotation.RotateVec(light.Offset);
                        Assert.That(lightPosition, Is.EqualTo(position), prototype.ID + ": the source must stay at the post base.");
                    }
                    em.DeleteEntity(post);
                }
                Assert.That(checkedLights, Is.GreaterThanOrEqualTo(30));
                TestContext.Out.WriteLine($"Checked {checkedLights} powered lamp-post variants at four rotations.");
                em.DeleteEntity(map);
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }
}

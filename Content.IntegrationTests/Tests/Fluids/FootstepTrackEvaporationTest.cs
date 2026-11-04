using System;
using System.Collections.Generic;
using System.Numerics;
using Content.Server.Decals;
using Content.Server.Fluids.Components;
using Content.Server.Fluids.EntitySystems;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Fluids;

public sealed class FootstepTrackEvaporationTest
{
    [Test]
    public async Task TracksDryByReagentSpeedAndMixedStainsRemain()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var em = pair.Server.EntMan;
            var walkers = new List<EntityUid>();
            var puddles = new List<EntityUid>();
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            string[] names = ["SpaceLube", "Water", "Holywater", "EvaporatingMix", "Blood", "BloodMix"];
            await pair.Server.WaitAssertion(() =>
            {
                var maps = em.System<SharedMapSystem>();
                var spill = em.System<PuddleSystem>();
                // Keep the test floor connected so adding separated puddle lanes cannot split the grid.
                for (var y = 0; y < names.Length * 3; y++)
                    for (var x = 0; x < 3; x++)
                        maps.SetTile(map.Grid, new Vector2i(x, y), map.Tile.Tile);
                for (var row = 0; row < names.Length; row++)
                {
                    var solution = names[row] switch
                    {
                        "EvaporatingMix" => new Solution("Water", FixedPoint2.New(10)),
                        "BloodMix" => new Solution("Water", FixedPoint2.New(18)),
                        var reagent => new Solution(reagent, FixedPoint2.New(20)),
                    };
                    if (names[row] == "EvaporatingMix")
                        solution.AddReagent("Holywater", FixedPoint2.New(10));
                    if (names[row] == "BloodMix")
                        solution.AddReagent("Blood", FixedPoint2.New(2));

                    var tile = maps.GetTileRef(map.Grid, map.Grid.Comp, new Vector2i(1, row * 3));
                    Assert.That(spill.TrySpillAt(tile, solution, out var puddle, sound: false, tileReact: false),
                        Is.True, $"Create the {names[row]} source puddle without recursive tile reactions.");
                    puddles.Add(puddle);
                    var walker = em.SpawnEntity("MobHuman", new EntityCoordinates(map.Grid, 0.5f, row * 3 + 0.5f));
                    em.EnsureComponent<FootstepTrackComponent>(walker);
                    walkers.Add(walker);
                }
            });
            await pair.RunTicksSync(1);
            for (var step = 1; step <= 2; step++)
            {
                var x = step;
                await pair.Server.WaitPost(() =>
                {
                    var transform = em.System<SharedTransformSystem>();
                    for (var row = 0; row < walkers.Count; row++)
                        transform.SetCoordinates(walkers[row], new EntityCoordinates(map.Grid, x + 0.5f, row * 3 + 0.5f));
                });
                await pair.RunTicksSync(1);
            }

            bool HasTrack(int row) => em.System<DecalSystem>().GetDecalsInRange(map.Grid,
                new Vector2(2, row * 3), 0.75f, decal => decal.Cleanable).Count > 0;
            await pair.Server.WaitAssertion(() =>
            {
                for (var row = 0; row < names.Length; row++)
                    Assert.That(HasTrack(row), Is.True, $"Walking through {names[row]} must leave a track.");
                // Drying must depend on the liquid that made the track, not a surviving source puddle.
                foreach (var puddle in puddles)
                    em.DeleteEntity(puddle);
            });

            await pair.RunTicksSync((int) Math.Ceiling(timing.TickRate * 15.0));
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(HasTrack(3), Is.False, "Both evaporating reagents dry independently in the same track.");
                Assert.That(HasTrack(1), Is.True, "Pure water must retain its original 25-second lifetime.");
                Assert.That(HasTrack(0), Is.True, "Space lube evaporates more slowly than water.");
            });

            await pair.RunTicksSync((int) Math.Ceiling(timing.TickRate * 15.0));
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(HasTrack(1), Is.False, "Water tracks must disappear even behind an earlier slow-drying track.");
                Assert.That(HasTrack(2), Is.False, "Holy water must dry just like its puddles.");
                Assert.That(HasTrack(0), Is.True);
            });

            await pair.RunTicksSync((int) Math.Ceiling(timing.TickRate * 50.0));
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(HasTrack(0), Is.False, "Space lube tracks must eventually evaporate too.");
                Assert.That(HasTrack(4), Is.True, "Blood does not evaporate.");
                Assert.That(HasTrack(5), Is.True, "A mostly-water mixture with blood must leave a persistent stain.");
            });
            await pair.CleanReturnAsync();
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine(exception);
            throw;
        }
    }
}

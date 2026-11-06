using System.Linq;
using System.Numerics;
using Content.Server._Triad.Shipyard;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Hands.Systems;
using Content.Shared.Interaction;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

public sealed class ShipSaveVaccinatorTest
{
    [Test]
    public async Task ConstructAndSaveVaccinator()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var em = pair.Server.EntMan;
            EntityUid frame = default;
            await pair.Server.WaitAssertion(() =>
            {
                em.System<SharedMapSystem>().SetTile(map.Grid, new Vector2i(1, 0), new Tile(1));
                var userCoords = map.GridCoords.Offset(Vector2.UnitX);
                var user = em.SpawnEntity("MobHuman", userCoords);
                frame = em.SpawnEntity("MachineFrame", map.GridCoords);
                Assert.That(em.GetComponent<TransformComponent>(frame).Anchored, Is.True);
                var parts = em.GetComponent<MachineFrameComponent>(frame);
                var containers = em.System<SharedContainerSystem>();
                var board = em.SpawnEntity("VaccinatorMachineCircuitboard", map.GridCoords);
                Assert.That(containers.Insert(board, parts.BoardContainer), Is.True);
                foreach (var id in new[] { "MatterBinStockPart", "MicroManipulatorStockPart", "CableApcStack10", "Beaker" })
                    Assert.That(containers.Insert(em.SpawnEntity(id, map.GridCoords), parts.PartContainer), Is.True, id);

                var frameSystem = em.System<MachineFrameSystem>();
                frameSystem.RegenerateProgress(parts);
                Assert.That(frameSystem.IsComplete(parts), Is.True);
                var screwdriver = em.SpawnEntity("Screwdriver", userCoords);
                Assert.That(em.System<HandsSystem>().TryPickupAnyHand(user, screwdriver), Is.True);
                Assert.That(em.System<SharedInteractionSystem>().InteractUsing(user, screwdriver, frame, map.GridCoords), Is.True);
            });
            await pair.Server.WaitRunTicks(120);
            await pair.Server.WaitAssertion(() =>
            {
                Assert.That(em.EntityExists(frame), Is.False);
                var vaccinator = em.EntityQuery<MetaDataComponent, TransformComponent>(true)
                    .Single(e => e.Item1.EntityPrototype?.ID == "Vaccinator" && e.Item2.GridUid == map.Grid.Owner);
                Assert.That(vaccinator.Item1.EntityLifeStage, Is.EqualTo(EntityLifeStage.MapInitialized));
                Assert.That(vaccinator.Item2.Anchored, Is.True);
                Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid, out var yaml, out _), Is.True);
                Assert.That(yaml, Does.Contain("proto: Vaccinator"));
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

using System.Linq;
using System.Numerics;
using Content.Server.Construction;
using Content.Server.Construction.Components;
using Content.Server.Lathe;
using Content.Shared._radiant.Artisan;
using Content.Shared.Construction;
using Content.Shared.Construction.Components;
using Content.Shared.Interaction;
using Content.Shared.Lathe;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class ArtisanAcquisitionTest
{
    [Test]
    public async Task ServiceTechfabsProvideSuppliesBoardsAndWorkingAssemblyKits()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var containers = em.System<SharedContainerSystem>();
                var construction = em.System<ConstructionSystem>();
                var actor = em.SpawnEntity("MobHumanDummy", map.GridCoords.Offset(new Vector2(2, 0)));
                var multitool = em.SpawnEntity("Multitool", map.GridCoords.Offset(new Vector2(2, 0)));
                var powered = new[] { "RadiantArtisanKiln", "RadiantArtisanFurnace", "RadiantAutomaticBottler" };
                var mechanical = new[] { "RadiantPotteryWheel", "RadiantArtisanAnvil", "RadiantGlassworkingBench", "RadiantQuenchingBath", "RadiantFermentationBarrel" };
                var expected = new[] { "RadiantClay", "RadiantArtisanTongs", "RadiantArtisanHammer", "RadiantArtisanBlowpipe", "RadiantArtisanPaddle", "RadiantArtisanBottle" }
                    .Concat(powered.Select(id => id + "MachineCircuitboard"))
                    .Concat(mechanical.Select(id => id + "Flatpack")).ToArray();
                foreach (var latheId in new[] { "ServiceTechFab", "McServiceTechFab" })
                {
                    var lathe = em.SpawnEntity(latheId, map.GridCoords.Offset(new Vector2(5, 0)));
                    var available = em.System<LatheSystem>().GetAvailableRecipes(lathe, em.GetComponent<LatheComponent>(lathe));
                    foreach (var id in expected)
                        Assert.That(available.Exists(recipe => recipe.Id == id), Is.True, $"{latheId}: missing {id}");
                    em.DeleteEntity(lathe);
                }

                foreach (var id in powered)
                {
                    var machine = em.SpawnEntity(id, map.GridCoords);
                    var data = em.GetComponent<MachineComponent>(machine);
                    var board = data.BoardContainer.ContainedEntities.Single();
                    Assert.That(em.GetComponent<MetaDataComponent>(board).EntityPrototype!.ID, Is.EqualTo(id + "MachineCircuitboard"));
                    Assert.That(em.GetComponent<MachineBoardComponent>(board).Prototype.Id, Is.EqualTo(id));
                    var item = em.SpawnEntity("RadiantArtisanBottle", map.GridCoords);
                    Assert.That(containers.Insert(item, containers.GetContainer(machine, ArtisanStationComponent.Container)), Is.True);
                    em.GetComponent<ArtisanStationComponent>(machine).Remaining = 10;
                    em.EventBus.RaiseLocalEvent(machine, new MachineDeconstructedEvent());
                    Assert.That(em.EntityExists(item), Is.True);
                    Assert.That(containers.GetContainer(machine, ArtisanStationComponent.Container).ContainedEntities, Is.Empty,
                        "Dismantling a busy machine must release its input items.");
                    em.DeleteEntity(item);
                    Assert.That(construction.ChangeNode(machine, actor, "machineFrame"), Is.True);
                    // ChangeNode queues the old structure for deletion; clear it before the same-tick unpack checks.
                    if (em.EntityExists(machine)) em.DeleteEntity(machine);
                    Assert.That(containers.TryGetContainingContainer((board, null, null), out var frameContainer), Is.True);
                    var frame = frameContainer.Owner;
                    var frameData = em.GetComponent<MachineFrameComponent>(frame);
                    em.System<MachineFrameSystem>().RegenerateProgress(frameData);
                    Assert.That(em.System<MachineFrameSystem>().IsComplete(frameData), Is.True, id);
                    Assert.That(construction.ChangeNode(frame, actor, "machine"), Is.True);
                    if (em.EntityExists(frame)) em.DeleteEntity(frame);
                    Assert.That(containers.TryGetContainingContainer((board, null, null), out var rebuiltContainer), Is.True);
                    var rebuilt = rebuiltContainer.Owner;
                    Assert.That(em.GetComponent<MetaDataComponent>(rebuilt).EntityPrototype!.ID, Is.EqualTo(id));
                    Assert.That(em.GetComponent<MachineComponent>(rebuilt).BoardContainer.ContainedEntities, Has.Count.EqualTo(1),
                        "Reassembly must reuse the original board without duplicating it.");
                    Assert.That(em.GetComponent<ArtisanStationComponent>(rebuilt).Busy, Is.False);
                    em.DeleteEntity(rebuilt);
                }

                foreach (var id in mechanical)
                {
                    var kit = em.SpawnEntity(id + "Flatpack", map.GridCoords);
                    var click = new InteractUsingEvent(actor, multitool, kit, map.GridCoords);
                    em.EventBus.RaiseLocalEvent(kit, click);
                    Assert.That(click.Handled, Is.True);
                    EntityUid? assembled = null;
                    var query = em.EntityQueryEnumerator<ArtisanStationComponent, MetaDataComponent>();
                    while (query.MoveNext(out var uid, out _, out var metadata))
                        if (metadata.EntityPrototype?.ID == id) assembled = uid;
                    Assert.That(assembled, Is.Not.Null, $"{id}: the printed kit must unpack into a working workstation.");
                    Assert.That(containers.GetContainer(assembled!.Value, ArtisanStationComponent.Container).ContainedEntities, Is.Empty);
                    em.DeleteEntity(assembled.Value);
                    if (em.EntityExists(kit)) em.DeleteEntity(kit);
                }
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }
}

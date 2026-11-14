using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._HL.Shipyard;
using Content.Server._Triad.Shipyard;
using Content.Server.Construction;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests._radiant.Shipyard;

[TestFixture]
public sealed class ShipSaveBlastDoorTest
{
    [TestCase(false)]
    [TestCase(true)]
    public async Task ConstructedBlastDoorKeepsInstalledBoardAndAccess(bool restricted)
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var map = await pair.CreateTestMap();
        var em = pair.Server.EntMan;

        await pair.Server.WaitAssertion(() =>
        {
            const string boardSlot = "board";
            var containers = em.System<SharedContainerSystem>();
            var construction = em.System<ConstructionSystem>();
            var access = em.System<AccessReaderSystem>();
            var frame = em.SpawnEntity("BlastDoorFrame", map.GridCoords);
            var board = em.SpawnEntity("DoorElectronics", map.GridCoords);
            if (restricted)
                access.SetAccesses((board, em.GetComponent<AccessReaderComponent>(board)),
                    new List<ProtoId<AccessLevelPrototype>> { "Engineering" });

            Assert.That(construction.AddContainer(frame, boardSlot), Is.True);
            Assert.That(containers.Insert(board, containers.EnsureContainer<Container>(frame, boardSlot)), Is.True);
            var xform = em.GetComponent<TransformComponent>(frame);
            if (!xform.Anchored)
                Assert.That(em.System<SharedTransformSystem>().AnchorEntity(frame, xform), Is.True);
            Assert.That(construction.ChangeNode(frame, null, "blastdoor"), Is.True);
            if (em.EntityExists(frame))
                em.DeleteEntity(frame);

            Assert.That(containers.TryGetContainingContainer(board, out var installed), Is.True);
            Assert.That(em.GetComponent<MetaDataComponent>(installed.Owner).EntityPrototype?.ID, Is.EqualTo("BlastDoorOpen"));
            Assert.That(installed.ContainedEntities, Is.EquivalentTo(new[] { board }));

            Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid.Owner, out var yaml, out _), Is.True);
            using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubShipLoadYaml(yaml, out _));
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "constructed blast door", out var loaded,
                offset: new Vector2(20, 20)), Is.True);

            var query = em.EntityQueryEnumerator<MetaDataComponent, TransformComponent>();
            var doors = new List<EntityUid>();
            while (query.MoveNext(out var uid, out var meta, out var transform))
            {
                if (transform.GridUid == loaded.Value.Owner && meta.EntityPrototype?.ID == "BlastDoorOpen")
                    doors.Add(uid);
            }

            var restored = doors.Single();
            var restoredBoard = containers.GetContainer(restored, boardSlot).ContainedEntities.Single();
            Assert.That(em.GetComponent<MetaDataComponent>(restoredBoard).EntityPrototype?.ID, Is.EqualTo("DoorElectronics"));
            var doorAccess = em.GetComponent<AccessReaderComponent>(restored);
            Assert.That(access.IsAllowed([], [], restored, doorAccess), Is.EqualTo(!restricted));
            Assert.That(access.IsAllowed(["Engineering"], [], restored, doorAccess), Is.True);
        });
        await pair.CleanReturnAsync();
    }
}

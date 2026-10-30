using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server._radiant.Machines;
using Content.Shared.Access;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DragDrop;
using Content.Shared.FixedPoint;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class ChemMasterDumpTest
{
    [Test]
    public async Task DraggedMixturesRespectPerReagentLimitsAccessAndDatabaseUpdates()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var em = pair.Server.EntMan;
            var key = "test-chemical-dump-" + Guid.NewGuid().ToString("N");
            EntityUid persistent = default, ordinary = default, visitor = default, agent = default, privateBeaker = default;
            await pair.Server.WaitAssertion(() =>
            {
                visitor = Actor("MedicalPhoenix");
                agent = Actor("MedicalPhoenixAgent");
                ordinary = em.SpawnEntity("ChemMaster", map.GridCoords);
                persistent = em.CreateEntityUninitialized("ChemMasterPhoenixPersistent", map.GridCoords);
                em.GetComponent<PersistentMachineComponent>(persistent).StorageId = key;
                em.InitializeAndStartEntity(persistent);
                privateBeaker = Beaker();
                Drag(privateBeaker, persistent, agent);
                Assert.That(Contents(privateBeaker).Volume, Is.EqualTo(FixedPoint2.New(4400)), "Pending database hydration blocks dumping.");
            });
            var ready = false;
            for (var i = 0; i < 250 && !ready; i++)
            {
                await pair.Server.WaitAssertion(() =>
                {
                    var storage = em.GetComponent<PersistentMachineComponent>(persistent);
                    Assert.That(storage.Failed, Is.False);
                    ready = storage.Available;
                });
                if (!ready) { await pair.RunTicksSync(1); await Task.Delay(10); }
            }
            Assert.That(ready, Is.True);
            await pair.Server.WaitAssertion(() =>
            {
                var input = Beaker();
                var buffer = Buffer(ordinary);
                buffer.AddReagent("Water", 900);
                Drag(input, ordinary, visitor);
                Assert.That(buffer.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(1000)));
                Assert.That(buffer.GetTotalPrototypeQuantity("Ethanol"), Is.EqualTo(FixedPoint2.New(1000)));
                Assert.That(buffer.GetTotalPrototypeQuantity("Blood"), Is.EqualTo(FixedPoint2.New(1000)), "DNA variants share one type limit.");
                Assert.That(buffer.Volume, Is.EqualTo(FixedPoint2.New(3000)));
                var remainder = Contents(input);
                Assert.That(remainder.GetTotalPrototypeQuantity("Water"), Is.EqualTo(FixedPoint2.New(1400)));
                Assert.That(remainder.GetTotalPrototypeQuantity("Ethanol"), Is.EqualTo(FixedPoint2.New(500)));
                Assert.That(remainder.GetTotalPrototypeQuantity("Blood"), Is.EqualTo(FixedPoint2.New(400)));
                Drag(input, ordinary, visitor);
                Assert.That(remainder.Volume, Is.EqualTo(FixedPoint2.New(2300)), "A repeated dump into a full buffer must retain the source.");

                Drag(privateBeaker, persistent, visitor);
                Assert.That(Contents(privateBeaker).Volume, Is.EqualTo(FixedPoint2.New(4400)));
                Assert.That(Buffer(persistent).Volume, Is.EqualTo(FixedPoint2.Zero), "Dumping must not bypass support-agent access.");
                Drag(privateBeaker, persistent, agent);
                Assert.That(Buffer(persistent).Volume, Is.EqualTo(FixedPoint2.New(3000)));
                Assert.That(Contents(privateBeaker).Volume, Is.EqualTo(FixedPoint2.New(1400)));
                Assert.That(Buffer(persistent).Contents.Where(r => r.Reagent.Prototype == "Blood")
                    .SelectMany(r => r.Reagent.Data!).OfType<DnaData>().Select(d => d.DNA),
                    Is.EquivalentTo(new[] { "donor-a", "donor-b" }));
            });
            // Observe automatic persistence without explicitly requesting a save.
            var database = pair.Server.ResolveDependency<IServerDbManager>();
            var written = false;
            for (var i = 0; i < 250 && !written; i++)
            {
                var row = await database.GetMachineStorageAsync(key);
                written = row?.Payload.Contains("donor-a") == true && row.Payload.Contains("donor-b");
                if (!written) { await pair.RunTicksSync(1); await Task.Delay(10); }
            }
            Assert.That(written, Is.True, "Dragging must notify persistence, not just mutate the buffer in memory.");

            EntityUid Actor(string tag)
            {
                var uid = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                em.AddComponent<AccessComponent>(uid);
                Assert.That(em.System<SharedAccessSystem>().TrySetTags(uid, new ProtoId<AccessLevelPrototype>[] { tag }), Is.True);
                return uid;
            }
            EntityUid Beaker()
            {
                var uid = em.SpawnEntity("LargeBeaker", map.GridCoords);
                var contents = Contents(uid);
                contents.MaxVolume = 5000;
                contents.CanReact = false;
                contents.AddReagent("Water", 1500);
                contents.AddReagent("Ethanol", 1500);
                contents.AddReagent(new ReagentId("Blood", new List<ReagentData> { new DnaData { DNA = "donor-a" } }), 700);
                contents.AddReagent(new ReagentId("Blood", new List<ReagentData> { new DnaData { DNA = "donor-b" } }), 700);
                return uid;
            }
            Solution Contents(EntityUid uid)
            {
                Assert.That(em.System<SharedSolutionContainerSystem>().TryGetFitsInDispenser(uid, out _, out var solution), Is.True);
                return solution!;
            }
            Solution Buffer(EntityUid uid)
            {
                Assert.That(em.System<SharedSolutionContainerSystem>().TryGetSolution(uid, SharedChemMaster.BufferSolutionName, out _, out var solution), Is.True);
                return solution!;
            }
            void Drag(EntityUid source, EntityUid target, EntityUid user)
            {
                var dragged = new DragDropDraggedEvent(user, target);
                em.EventBus.RaiseLocalEvent(source, ref dragged);
            }
        }
        finally { await pair.CleanReturnAsync(); }
    }
}

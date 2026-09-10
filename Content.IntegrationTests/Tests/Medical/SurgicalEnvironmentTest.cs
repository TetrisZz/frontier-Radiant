using System.Linq;
using Content.Server._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Fluids.Components;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Mech.Components;
using Content.Shared.StepTrigger.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class SurgicalEnvironmentTest
{
    [Test]
    public async Task ActiveHandDrainAndMechProtection()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
        var entities = server.ResolveDependency<IEntityManager>();
        await server.WaitAssertion(() =>
        {
            var map = server.ResolveDependency<IMapManager>().CreateMap();
            var coordinates = new MapCoordinates(0, 0, map);
            var surgeon = entities.SpawnEntity("MobHuman", coordinates);
            var patient = entities.SpawnEntity("MobHuman", coordinates);
            var surgery = entities.System<SurgerySystem>();
            var hands = entities.System<SharedHandsSystem>();
            var scalpel = entities.SpawnEntity("Scalpel", coordinates);
            var shard = entities.SpawnEntity("ShardGlass", coordinates);
            Assert.That(hands.TryPickupAnyHand(surgeon, scalpel), Is.True);
            Assert.That(hands.TryPickupAnyHand(surgeon, shard), Is.True);
            var handState = entities.GetComponent<HandsComponent>(surgeon);
            var step = entities.SpawnEntity("SurgeryStepOpenIncisionScalpel", coordinates);
            foreach (var hand in handState.Hands.Keys)
            {
                hands.SetActiveHand(surgeon, hand);
                Assert.That(hands.TryGetActiveItem(surgeon, out var active), Is.True);
                Assert.That(surgery.CanPerformStep(surgeon, patient, BodyPartType.Head, step, false, out _, out _, out var tools), Is.True);
                Assert.That(tools, Is.EquivalentTo(new[] { active!.Value }));
                Assert.That(surgery.GetStepSuccessRate(step, tools),
                    Is.EqualTo(active == scalpel ? 1f : .7f).Within(.001));
                var clamp = entities.SpawnEntity("SurgeryStepAdultClamp", coordinates);
                Assert.That(surgery.CanPerformStep(surgeon, patient, BodyPartType.Head, clamp, false, out _, out _, out _), Is.False);
            }

            Assert.That(hands.TryDrop(surgeon, shard), Is.True);
            var hemostat = entities.SpawnEntity("Hemostat", coordinates);
            Assert.That(hands.TryPickupAnyHand(surgeon, hemostat), Is.True);
            foreach (var hand in handState.Hands.Keys)
            {
                hands.SetActiveHand(surgeon, hand);
                hands.TryGetActiveItem(surgeon, out var active);
                Assert.That(surgery.CanPerformStep(surgeon, patient, BodyPartType.Head, step, false, out _, out _, out _),
                    Is.EqualTo(active == scalpel), "The inactive scalpel must not make an active hemostat valid.");
            }

            var puddle = entities.SpawnEntity("PuddleBlood", coordinates);
            Assert.That(surgery.IsSurgicalPuddleHazard(puddle), Is.True);
            var drain = entities.SpawnEntity("FloorDrain", coordinates);
            Assert.That(surgery.IsSurgicalPuddleHazard(puddle), Is.False, "An operating drain removes the puddle penalty.");
            var solutions = entities.System<SharedSolutionContainerSystem>();
            Assert.That(solutions.TryGetSolution(drain, DrainComponent.SolutionName, out var buffer, out var bufferLiquid), Is.True);
            solutions.TryAddReagent(buffer!.Value, "Water", bufferLiquid!.AvailableVolume);
            Assert.That(surgery.IsSurgicalPuddleHazard(puddle), Is.True, "A full drain does not protect the room.");
            Assert.That(solutions.TryGetSolution(puddle, "puddle", out var puddleSolution, out _), Is.True);
            solutions.RemoveAllSolution(puddleSolution!.Value);
            Assert.That(surgery.IsSurgicalPuddleHazard(puddle), Is.False, "An empty puddle awaiting removal is harmless.");

            foreach (var mechId in new[] { "MechRipley", "MechEmu", "MechClarke" })
            {
                var pilot = entities.SpawnEntity("MobHuman", coordinates);
                var mech = entities.SpawnEntity(mechId, coordinates);
                Assert.That(entities.System<Content.Shared.Mech.EntitySystems.SharedMechSystem>().TryInsert(mech, pilot), Is.True);
                var glass = entities.SpawnEntity("ShardGlass", coordinates);
                var trip = new StepTriggeredOffEvent(glass, pilot);
                entities.EventBus.RaiseLocalEvent(glass, ref trip);
                Assert.That(entities.System<SharedBodySystem>().GetBodyChildren(pilot)
                    .Any(p => entities.HasComponent<EmbeddedGlassComponent>(p.Id)), Is.False);
                Assert.That(entities.System<Content.Shared.Mech.EntitySystems.SharedMechSystem>().TryEject(mech), Is.True);
            }
        });
    }
}

using System.Collections.Generic;
using Content.Server._radiant.GhostRoles;
using Content.Server.Ghost.Roles.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Content.Server.StationEvents.Components;
using Content.Shared.EntityEffects;
using Content.Shared.EntityEffects.Effects;
using Content.Shared.GameTicking.Components;
using Content.Shared.Station.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.IntegrationTests.Tests._radiant;

public sealed class AutomaticGhostRoleTest
{
    [Test]
    public async Task FixturesCannotGainAutomaticRolesFromEffectsOrStationEvents()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var station = em.Spawn();
                em.AddComponent<StationDataComponent>(station);
                em.AddComponent<StationEventEligibleComponent>(station);
                em.System<StationSystem>().AddGridToStation(station, map.Grid.Owner);
                var fixtures = new[]
                {
                    "VendingMachineGames", "HydroponicsTrayEmpty", "hydroponicsSoil", "ServiceTechFab", "Airlock", "ClosetTool"
                };
                var targets = new List<EntityUid>();
                var effect = new MakeSentient();
                foreach (var prototype in fixtures)
                {
                    var target = em.SpawnEntity(prototype, map.GridCoords);
                    targets.Add(target);
                    Assert.That(em.GetComponent<AutomaticGhostRoleBlockerComponent>(target).Enabled, Is.True, prototype);
                    effect.Effect(new EntityEffectBaseArgs(target, em));
                    Assert.That(em.HasComponent<GhostRoleComponent>(target), Is.False, prototype);
                    Assert.That(em.HasComponent<GhostTakeoverAvailableComponent>(target), Is.False, prototype);
                    // Cover legacy maps or future prototypes which still carry an event target.
                    MarkAsCandidate(target);
                }

                var animal = em.SpawnEntity("MobMonkey", map.GridCoords);
                Assert.That(em.HasComponent<AutomaticGhostRoleBlockerComponent>(animal), Is.False);
                effect.Effect(new EntityEffectBaseArgs(animal, em));
                Assert.That(em.HasComponent<GhostRoleComponent>(animal), Is.True, "Animals must still be eligible for sentience.");
                em.RemoveComponent<GhostRoleComponent>(animal);
                em.RemoveComponent<GhostTakeoverAvailableComponent>(animal);
                MarkAsCandidate(animal);

                var rule = em.Spawn("NFRandomSentience");
                var started = new GameRuleStartedEvent(rule, "NFRandomSentience");
                em.EventBus.RaiseLocalEvent(rule, ref started);
                Assert.That(em.HasComponent<GhostRoleComponent>(animal), Is.True, "The station event must continue to choose eligible animals.");
                foreach (var target in targets)
                    Assert.That(em.HasComponent<GhostRoleComponent>(target), Is.False, em.GetComponent<MetaDataComponent>(target).EntityPrototype!.ID);

                void MarkAsCandidate(EntityUid target)
                {
                    // Apply legacy prototype data through the normal serializer rather than bypassing component access rules.
                    var node = new MappingDataNode { { "flavorKind", new ValueDataNode("station-event-random-sentience-flavor-mechanical") } };
                    var candidate = pair.Server.ResolveDependency<ISerializationManager>().Read<SentienceTargetComponent>(node);
                    em.RemoveComponent<SentienceTargetComponent>(target);
                    em.AddComponent(target, candidate);
                }
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

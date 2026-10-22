using System.Numerics;
using Content.Server._radiant.Fishing;
using Content.Shared._radiant.Fishing.Components;
using Content.Shared._radiant.Fishing.Events;
using Content.Shared._radiant.Fishing.Systems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Hands.Components;
using Robust.Shared.GameObjects;
using ServerHands = Content.Server.Hands.Systems.HandsSystem;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class FishingTackleTest
{
    [Test]
    public async Task SlotsCastingWearAndReplacement()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        EntityUid rod = default;
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var fishing = entities.System<FishingSystem>();
            var slots = entities.System<ItemSlotsSystem>();
            var hands = entities.System<ServerHands>();
            var user = entities.SpawnEntity("MobHumanDummy", map.GridCoords);
            rod = entities.SpawnEntity("FishingRod", map.GridCoords);
            var rodComp = entities.GetComponent<FishingRodComponent>(rod);
            hands.AddHand(user, "fishing-test", HandLocation.Right);
            Assert.That(hands.TryPickup(user, rod, "fishing-test", checkActionBlocker: false, animate: false), Is.True);
            Assert.That(fishing.HasUsableTackle(rod), Is.True, "New rods must have usable starter tackle");
            var line = fishing.GetTackle(rod, SharedFishingSystem.LineSlot)!.Value;
            var lineComp = entities.GetComponent<FishingLineComponent>(line);
            Assert.That(lineComp.Durability, Is.EqualTo(20));
            var hook = fishing.GetTackle(rod, SharedFishingSystem.HookSlot)!.Value;
            var upgradedHook = entities.SpawnEntity("RadiantFishingHookReinforced", map.GridCoords);
            var upgradedLine = entities.SpawnEntity("RadiantFishingLineReinforced", map.GridCoords);
            var wrongItem = entities.SpawnEntity("Crowbar", map.GridCoords);
            Assert.That(slots.TryInsert(rod, SharedFishingSystem.HookSlot, wrongItem, null), Is.False);
            Assert.That(slots.TryInsert(rod, SharedFishingSystem.LineSlot, upgradedHook, null), Is.False);

            Assert.That(slots.TryEject(rod, SharedFishingSystem.HookSlot, null, out _), Is.True);
            Assert.That(fishing.HasUsableTackle(rod), Is.False);
            var missingHookCast = new ThrowFishingLureActionEvent { Performer = user, Target = map.GridCoords.Offset(new Vector2(2, 0)) };
            entities.EventBus.RaiseLocalEvent(rod, missingHookCast);
            Assert.That(rodComp.FishingLure, Is.Null, "Cannot cast without a hook");
            Assert.That(slots.TryInsert(rod, SharedFishingSystem.HookSlot, hook, null), Is.True);
            Assert.That(slots.TryEject(rod, SharedFishingSystem.LineSlot, null, out _), Is.True);
            var missingLineCast = new ThrowFishingLureActionEvent { Performer = user, Target = map.GridCoords.Offset(new Vector2(2, 0)) };
            entities.EventBus.RaiseLocalEvent(rod, missingLineCast);
            Assert.That(rodComp.FishingLure, Is.Null, "Cannot cast without line");
            Assert.That(slots.TryInsert(rod, SharedFishingSystem.LineSlot, line, null), Is.True);

            var cast = new ThrowFishingLureActionEvent { Performer = user, Target = map.GridCoords.Offset(new Vector2(2, 0)) };
            entities.EventBus.RaiseLocalEvent(rod, cast);
            Assert.That(rodComp.FishingLure, Is.Not.Null);
            Assert.That(slots.TryEject(rod, SharedFishingSystem.HookSlot, null, out _), Is.False, "Cannot replace a cast hook");
            Assert.That(slots.TryEject(rod, SharedFishingSystem.LineSlot, null, out _), Is.False, "Cannot replace a cast line");
            Assert.That(slots.TryInsert(rod, SharedFishingSystem.HookSlot, upgradedHook, null), Is.False);
            var reel = new PullFishingLureActionEvent { Performer = user };
            entities.EventBus.RaiseLocalEvent(rod, reel);
            Assert.That(rodComp.FishingLure, Is.Null);
            Assert.That(lineComp.Durability, Is.EqualTo(19));

            Assert.That(slots.TryInsert(rod, SharedFishingSystem.LineSlot, upgradedLine, null), Is.True);
            Assert.That(slots.TryInsert(rod, SharedFishingSystem.HookSlot, upgradedHook, null), Is.True);
            Assert.That(fishing.GetTackle(rod, SharedFishingSystem.LineSlot), Is.EqualTo(upgradedLine));
            Assert.That(entities.GetComponent<FishingLineComponent>(upgradedLine).Durability, Is.EqualTo(40));
            Assert.That(entities.GetComponent<FishingHookComponent>(upgradedHook).EfficiencyMultiplier, Is.GreaterThan(1f));
            Assert.That(lineComp.Durability, Is.EqualTo(19), "Swapping out a line must preserve its wear");

            var upgradedLineComp = entities.GetComponent<FishingLineComponent>(upgradedLine);
            upgradedLineComp.Durability = 1;
            var lastCast = new ThrowFishingLureActionEvent { Performer = user, Target = map.GridCoords.Offset(new Vector2(2, 0)) };
            entities.EventBus.RaiseLocalEvent(rod, lastCast);
            var lastReel = new PullFishingLureActionEvent { Performer = user };
            entities.EventBus.RaiseLocalEvent(rod, lastReel);
            Assert.That(upgradedLineComp.Durability, Is.Zero);
            Assert.That(fishing.HasUsableTackle(rod), Is.False, "An exhausted line must prevent further casts");
        });
        await pair.RunTicksSync(2);
        await pair.Server.WaitAssertion(() =>
        {
            var fishing = pair.Server.ResolveDependency<IEntityManager>().System<FishingSystem>();
            Assert.That(fishing.GetTackle(rod, SharedFishingSystem.HookSlot), Is.Null, "A snapped line loses the hook");
            Assert.That(fishing.GetTackle(rod, SharedFishingSystem.LineSlot), Is.Null);
        });
        await pair.CleanReturnAsync();
    }
}

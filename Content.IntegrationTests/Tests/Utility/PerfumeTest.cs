using System;
using System.Linq;
using System.Numerics;
using Content.Server._radiant.Perfume;
using Content.Server._radiant.Shower;
using Content.Shared._radiant.Perfume;
using Content.Shared._Starlight.Scent.Components;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.VendingMachines;
using Robust.Client.GameObjects;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using ServerHands = Content.Server.Hands.Systems.HandsSystem;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class PerfumeTest
{
    [Test]
    public async Task ApplicationConsumptionReplacementWashingAndVending()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        EntityUid user = default;
        EntityUid target = default;
        EntityUid rose = default;
        EntityUid lavender = default;
        EntityUid soap = default;
        string? scentId = null;

        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var hands = entities.System<ServerHands>();
            var doAfter = entities.System<SharedDoAfterSystem>();
            user = entities.SpawnEntity("MobHumanDummy", map.GridCoords);
            target = entities.SpawnEntity("MobHumanDummy", map.GridCoords.Offset(new Vector2(1, 0)));
            scentId = entities.EnsureComponent<ScentComponent>(target).ScentId ??= "perfume-test-identity";
            rose = entities.SpawnEntity("PerfumeRose", map.GridCoords);
            hands.AddHand(user, "perfume-test-right", HandLocation.Right);
            hands.SetActiveHand(user, "perfume-test-right");
            entities.EnsureComponent<DoAfterComponent>(user);
            Assert.That(hands.TryPickup(user, rose, "perfume-test-right", checkActionBlocker: false, animate: false), Is.True);

            var useSelf = new UseInHandEvent(user);
            entities.EventBus.RaiseLocalEvent(rose, useSelf);
            Assert.That(useSelf.Handled, Is.True);
            var pending = entities.GetComponent<DoAfterComponent>(user).DoAfters;
            Assert.That(pending.Values.Any(action => action.Args.Event is ApplyPerfumeEvent && action.Args.Target == user), Is.True);
            Assert.That(entities.GetComponent<PerfumeComponent>(rose).Uses, Is.EqualTo(30), "Starting or cancelling an application must not spend perfume");
            foreach (var id in pending.Keys.ToArray())
                doAfter.Cancel(user, id);

            var useOther = new AfterInteractEvent(user, rose, target, map.GridCoords, true);
            entities.EventBus.RaiseLocalEvent(rose, useOther);
            Assert.That(useOther.Handled, Is.True);
        });

        await pair.RunTicksSync(90);
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var perfume = entities.System<PerfumeSystem>();
            var hands = entities.System<ServerHands>();
            var timing = pair.Server.ResolveDependency<IGameTiming>();
            var roseBottle = entities.GetComponent<PerfumeComponent>(rose);
            Assert.That(roseBottle.Uses, Is.EqualTo(29));
            Assert.That(entities.GetComponent<PerfumedComponent>(target).Fragrance, Is.EqualTo(roseBottle.Fragrance));
            Assert.That(entities.GetComponent<ScentComponent>(target).ScentId, Is.EqualTo(scentId));
            Assert.That(entities.HasComponent<PerfumedComponent>(user), Is.False, "The cancelled self-application must not complete");

            lavender = entities.SpawnEntity("PerfumeLavender", map.GridCoords);
            hands.AddHand(user, "perfume-test-left", HandLocation.Left);
            Assert.That(hands.TryPickup(user, lavender, "perfume-test-left", checkActionBlocker: false, animate: false), Is.True);
            var lavenderBottle = entities.GetComponent<PerfumeComponent>(lavender);
            Assert.That(perfume.TryApply((lavender, lavenderBottle), user, target), Is.True);
            var applied = entities.GetComponent<PerfumedComponent>(target);
            Assert.That(applied.Fragrance, Is.EqualTo(lavenderBottle.Fragrance), "New perfume replaces the old fragrance");
            Assert.That(applied.ExpiresAt, Is.EqualTo(timing.CurTime + TimeSpan.FromMinutes(20)));
            applied.ExpiresAt = timing.CurTime + TimeSpan.FromMinutes(1);
            Assert.That(perfume.TryApply((lavender, lavenderBottle), user, target), Is.True);
            Assert.That(applied.ExpiresAt, Is.EqualTo(timing.CurTime + TimeSpan.FromMinutes(20)), "Reapplying refreshes rather than stacking duration");
            lavenderBottle.Uses = 0;
            var expires = applied.ExpiresAt;
            Assert.That(perfume.TryApply((lavender, lavenderBottle), user, target), Is.False);
            Assert.That(applied.ExpiresAt, Is.EqualTo(expires));
            lavenderBottle.Uses = 10;
            var item = entities.SpawnEntity("Crowbar", map.GridCoords);
            Assert.That(perfume.TryApply((lavender, lavenderBottle), user, item), Is.False);
            Assert.That(lavenderBottle.Uses, Is.EqualTo(10));

            var observer = entities.SpawnEntity("MobHumanDummy", map.GridCoords.Offset(new Vector2(10, 0)));
            Assert.That(perfume.TryGetFragrance(target, observer, out _), Is.False, "No fragrance descriptions at a distance");
            var transform = entities.System<SharedTransformSystem>();
            transform.SetCoordinates(observer, map.GridCoords.Offset(new Vector2(3, 0)));
            Assert.That(perfume.TryGetFragrance(target, observer, out var description), Is.True);
            Assert.That(description, Is.EqualTo(Loc.GetString(lavenderBottle.Fragrance)));
            var message = new ExaminedEvent(new FormattedMessage(), target, observer, true, false);
            entities.EventBus.RaiseLocalEvent(target, message);
            Assert.That(message.GetTotalMessage().ToString(), Does.Contain(description));
            var wall = entities.SpawnEntity("WallSolid", map.GridCoords.Offset(new Vector2(2, 0)));
            Assert.That(perfume.TryGetFragrance(target, observer, out _), Is.False, "Cannot smell through a wall");
            entities.DeleteEntity(wall);
            transform.SetCoordinates(observer, map.GridCoords.Offset(new Vector2(10, 0)));
            Assert.That(perfume.TryApply((lavender, lavenderBottle), user, observer), Is.False, "Cannot apply perfume remotely");
            Assert.That(lavenderBottle.Uses, Is.EqualTo(10));

            soap = entities.SpawnEntity("Soap", map.GridCoords);
            hands.AddHand(user, "perfume-test-soap", HandLocation.Right);
            Assert.That(hands.TryPickup(user, soap, "perfume-test-soap", checkActionBlocker: false, animate: false), Is.True);
            var wash = new InteractUsingEvent(user, soap, target, map.GridCoords);
            entities.EventBus.RaiseLocalEvent(target, wash);
            Assert.That(wash.Handled, Is.True);
            Assert.That(entities.GetComponent<DoAfterComponent>(user).DoAfters.Values.Any(action => action.Args.Event is WashPerfumeEvent), Is.True);
        });

        await pair.RunTicksSync(220);
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var perfume = entities.System<PerfumeSystem>();
            var lavenderBottle = entities.GetComponent<PerfumeComponent>(lavender);
            Assert.That(entities.HasComponent<PerfumedComponent>(target), Is.False, "Soap must remove the applied fragrance");
            Assert.That(entities.GetComponent<ScentComponent>(target).ScentId, Is.EqualTo(scentId));
            Assert.That(perfume.TryApply((lavender, lavenderBottle), user, target), Is.True);
            var shower = entities.SpawnEntity("RadiantShower", map.GridCoords);
            var water = entities.System<ShowerWaterSystem>();
            water.Update(5);
            Assert.That(entities.HasComponent<PerfumedComponent>(target), Is.True, "A switched-off shower cannot wash perfume off");
            Assert.That(entities.System<ItemToggleSystem>().TryActivate(shower), Is.True);
            water.Update(5);
            Assert.That(entities.HasComponent<PerfumedComponent>(target), Is.False, "Standing under the active stream must wash off perfume");
            Assert.That(entities.GetComponent<ScentComponent>(target).ScentId, Is.EqualTo(scentId));
            Assert.That(perfume.TryApply((lavender, lavenderBottle), user, target), Is.True);
            entities.GetComponent<PerfumedComponent>(target).ExpiresAt = pair.Server.ResolveDependency<IGameTiming>().CurTime - TimeSpan.FromSeconds(1);
            Assert.That(perfume.TryGetFragrance(target, user, out _), Is.False, "Expired perfume must be hidden before deferred removal");

            var machine = entities.SpawnEntity("VendingMachineCosmetics", map.GridCoords);
            var inventory = entities.GetComponent<VendingMachineComponent>(machine).Inventory;
            foreach (var name in new[] { "CherryLiqueur", "TobaccoVanilla", "PearSilk", "WhiteTea", "MidnightCoffee", "SaltedSkin" })
            {
                Assert.That(inventory.ContainsKey("Perfume" + name), Is.True);
                entities.SpawnEntity("Perfume" + name, map.GridCoords);
            }
            entities.SpawnEntity("VendingMachineRestockCosmetics", map.GridCoords);
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            Assert.That(prototypes.Index<VendingMachineInventoryPrototype>("XomkaVendInventory").StartingInventory.Keys
                .Any(id => id.StartsWith("Perfume")), Is.False, "The existing HomkoMat inventory must remain unchanged");
        });

        await pair.RunTicksSync(70);
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            Assert.That(entities.HasComponent<PerfumedComponent>(target), Is.False, "Expired fragrance component must be removed");
        });
        await pair.Client.WaitAssertion(() =>
        {
            var entities = pair.Client.ResolveDependency<IEntityManager>();
            foreach (var (name, state) in new[]
            {
                ("Rose", "rose"), ("Lavender", "lavender"), ("Citrus", "citrus"),
                ("Pine", "pine"), ("Ocean", "ocean"), ("Vanilla", "vanilla"),
                ("CherryLiqueur", "rose"), ("TobaccoVanilla", "vanilla"), ("PearSilk", "citrus"),
                ("WhiteTea", "pine"), ("MidnightCoffee", "lavender"), ("SaltedSkin", "ocean"),
            })
            {
                var bottle = entities.SpawnEntity("Perfume" + name, MapCoordinates.Nullspace);
                var sprite = entities.GetComponent<SpriteComponent>(bottle);
                Assert.That(sprite.BaseRSI!.TryGetState(state, out _), Is.True);
                Assert.That(sprite.BaseRSI.TryGetState(state + "-inhand-left", out _), Is.False);
                Assert.That(sprite.BaseRSI.TryGetState(state + "-inhand-right", out _), Is.False);
                entities.DeleteEntity(bottle);
            }
            var machine = entities.SpawnEntity("VendingMachineCosmetics", MapCoordinates.Nullspace);
            var machineSprite = entities.GetComponent<SpriteComponent>(machine);
            foreach (var state in new[] { "normal", "off", "broken", "panel", "screen", "deny" })
                Assert.That(machineSprite.BaseRSI!.TryGetState(state, out _), Is.True);
            entities.DeleteEntity(machine);
        });
        await pair.CleanReturnAsync();
    }
}

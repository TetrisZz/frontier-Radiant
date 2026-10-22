using System;
using System.Linq;
using Content.Server.Atmos.EntitySystems;
using Content.Server.Power.EntitySystems;
using Content.Server.PowerCell;
using Content.Shared._radiant.Fishing.Components;
using Content.Shared._radiant.Social;
using Content.Shared.Atmos;
using Content.Shared.Buckle.Components;
using Content.Shared.Clothing;
using Content.Shared.Clothing.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Interaction;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;
using ServerHands = Content.Server.Hands.Systems.HandsSystem;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class SocialUpgradesTest
{
    [Test]
    public async Task GiftBoxesHairSelectionScannerPowerAndPoleControls()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        EntityUid user = default;
        EntityUid pole = default;
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var air = new GasMixture(Atmospherics.CellVolume) { Temperature = 293.15f };
            air.AdjustMoles(Gas.Oxygen, 21);
            air.AdjustMoles(Gas.Nitrogen, 79);
            em.System<AtmosphereSystem>().SetMapAtmosphere(map.MapUid, false, air);
            user = em.SpawnEntity("MobHuman", map.GridCoords);
            var other = em.SpawnEntity("MobHuman", map.GridCoords);
            var hands = em.System<ServerHands>();
            var containers = em.System<SharedContainerSystem>();
            foreach (var (box, deck) in new[] { ("BoxQuestionDeck", "QuestionDeck"), ("BoxDareDeck", "DareDeck"),
                         ("BoxAdultQuestionDeck", "AdultQuestionDeck"), ("BoxBlankCardDeck", "BlankCardDeck") })
            {
                var uid = em.SpawnEntity(box, map.GridCoords);
                var contents = containers.GetContainer(uid, "storagebase").ContainedEntities;
                Assert.That(contents, Has.Count.EqualTo(1));
                Assert.That(em.GetComponent<MetaDataComponent>(contents.Single()).EntityPrototype!.ID, Is.EqualTo(deck));
            }
            var appearance = em.GetComponent<HumanoidAppearanceComponent>(user);
            appearance.MarkingSet.RemoveCategory(MarkingCategories.Hair);
            em.System<SharedHumanoidAppearanceSystem>().AddMarking(user, "HumanHairLong", Color.Red, humanoid: appearance);
            var tie = em.SpawnEntity("ClothingHeadHairTie", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(user, tie), Is.True);
            var equip = new ClothingGotEquippedEvent(user, em.GetComponent<ClothingComponent>(tie));
            em.EventBus.RaiseLocalEvent(tie, ref equip);
            void Select(EntityUid actor, string style) => em.EventBus.RaiseLocalEvent(tie,
                new HairAccessorySelectMessage(style) { Actor = actor, UiKey = HairAccessoryUiKey.Key });
            Select(user, "HumanHairLong");
            Assert.That(em.GetComponent<HairAccessoryComponent>(tie).Style, Is.EqualTo("HumanHairPonytail"));
            Select(other, "HumanHairBun3");
            Assert.That(em.GetComponent<HairAccessoryComponent>(tie).Style, Is.EqualTo("HumanHairPonytail"));
            Select(user, "HumanHairBun3");
            Assert.That(appearance.MarkingSet.Markings[MarkingCategories.Hair].Single().MarkingId, Is.EqualTo("HumanHairBun3"));
            Assert.That(appearance.MarkingSet.Markings[MarkingCategories.Hair].Single().MarkingColors, Does.Contain(Color.Red));
            var unequip = new ClothingGotUnequippedEvent(user, em.GetComponent<ClothingComponent>(tie));
            em.EventBus.RaiseLocalEvent(tie, ref unequip);
            Assert.That(appearance.MarkingSet.Markings[MarkingCategories.Hair].Single().MarkingId, Is.EqualTo("HumanHairLong"));
            Assert.That(hands.TryDrop(user, tie), Is.True);
            var scanner = em.SpawnEntity("FishScanner", map.GridCoords);
            Assert.That(hands.TryPickupAnyHand(user, scanner), Is.True);
            var cells = em.System<PowerCellSystem>();
            Assert.That(cells.TryGetBatteryFromSlot(scanner, out var cell, out var battery), Is.True);
            var initialCharge = battery!.CurrentCharge;
            var fish = em.SpawnEntity("FishBass", map.GridCoords);
            var request = new AfterInteractEvent(user, scanner, fish, map.GridCoords, true);
            em.EventBus.RaiseLocalEvent(scanner, request);
            var scan = em.GetComponent<FishScannerComponent>(scanner);
            Assert.That(scan.LastScan!.Fish, Has.Count.EqualTo(1));
            Assert.That(battery.CurrentCharge, Is.EqualTo(initialCharge - 40));
            em.EventBus.RaiseLocalEvent(scanner, new AfterInteractEvent(user, scanner, fish, map.GridCoords, true));
            Assert.That(battery.CurrentCharge, Is.EqualTo(initialCharge - 40), "Cooldown prevents repeated charge use.");
            em.System<BatterySystem>().SetCharge(cell!.Value, 0);
            scan.ReadyAt = TimeSpan.Zero;
            var previous = scan.LastScan;
            em.EventBus.RaiseLocalEvent(scanner, new AfterInteractEvent(user, scanner, fish, map.GridCoords, true));
            Assert.That(scan.LastScan, Is.SameAs(previous), "An empty cell cannot produce a scan.");
            Assert.That(em.System<ItemSlotsSystem>().TryEject(scanner, "cell_slot", user, out _), Is.True);
            em.EventBus.RaiseLocalEvent(scanner, new AfterInteractEvent(user, scanner, fish, map.GridCoords, true));
            Assert.That(scan.LastScan, Is.SameAs(previous), "A missing cell cannot produce a scan.");

            pole = em.SpawnEntity("Pole", map.GridCoords);
            void Command(EntityUid actor, DancePoleAction action, int value = 0) => em.EventBus.RaiseLocalEvent(pole,
                new DancePoleMessage(action, value) { Actor = actor, UiKey = DancePoleUiKey.Key });
            Command(user, DancePoleAction.Dance, (int) PoleDanceMove.Turn);
            Assert.That(em.GetComponent<DancePoleComponent>(pole).Dancer, Is.EqualTo(user));
            Assert.That(em.GetComponent<PoleDancerComponent>(user).Move, Is.EqualTo(PoleDanceMove.Turn));
            Assert.That(em.GetComponent<BuckleComponent>(user).BuckledTo, Is.EqualTo(pole));
            Command(other, DancePoleAction.Dance);
            Assert.That(em.HasComponent<PoleDancerComponent>(other), Is.False);
            Command(user, DancePoleAction.LightToggle);
            Command(user, DancePoleAction.LightColor, 2);
            Command(user, DancePoleAction.Finish, 1);
            Assert.That(em.GetComponent<DancePoleComponent>(pole).LightEnabled, Is.True);
            Assert.That(em.GetComponent<DancePoleComponent>(pole).Finish, Is.EqualTo(1));
            Command(user, DancePoleAction.Stop);
            Assert.That(em.HasComponent<PoleDancerComponent>(user), Is.False);
            Assert.That(em.GetComponent<BuckleComponent>(user).BuckledTo, Is.Null);
            Assert.That(em.GetComponent<DancePoleComponent>(pole).Dancer, Is.Null);
            Command(other, DancePoleAction.Dance, 99);
            Assert.That(em.HasComponent<PoleDancerComponent>(other), Is.False);
            Command(user, DancePoleAction.Dance, (int) PoleDanceMove.Sway);
        });
        await pair.RunTicksSync(10);
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            Assert.That(em.HasComponent<PoleDancerComponent>(user), Is.True, "Dance must continue across server updates.");
            var stop = new DancePoleMessage(DancePoleAction.Stop) { Actor = user, UiKey = DancePoleUiKey.Key };
            em.EventBus.RaiseLocalEvent(pole, stop);
            Assert.That(em.HasComponent<PoleDancerComponent>(user), Is.False);
        });
        TestContext.Out.WriteLine("Gift box contents, restricted hair selection/restoration, battery consumption and pole controls all passed.");
        await pair.CleanReturnAsync();
    }
}

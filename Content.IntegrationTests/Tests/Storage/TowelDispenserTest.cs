using System.Linq;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.GameObjects;
using ServerHands = Content.Server.Hands.Systems.HandsSystem;

namespace Content.IntegrationTests.Tests.Storage;

[TestFixture]
public sealed class TowelDispenserTest
{
    [Test]
    public async Task TowelsCanBeDispensedAndReturnedWithThreeFillLevels()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.ResolveDependency<IEntityManager>();
            var bins = em.System<BinSystem>();
            var appearance = em.System<SharedAppearanceSystem>();
            var empty = em.SpawnEntity("TowelDispenser", map.GridCoords);
            Assert.That(em.GetComponent<BinComponent>(empty).Items, Is.Empty);
            var dispenser = em.SpawnEntity("TowelDispenserFilled", map.GridCoords);
            var bin = em.GetComponent<BinComponent>(dispenser);
            Assert.That(bin.Items, Has.Count.EqualTo(10));
            Assert.That(bin.ItemContainer.Count, Is.EqualTo(10));

            void CheckLevel(int expected)
            {
                Assert.That(appearance.TryGetData<int>(dispenser, StorageFillVisuals.FillLevel, out var level), Is.True);
                Assert.That(level, Is.EqualTo(expected));
            }

            CheckLevel(2);
            var overflow = em.SpawnEntity("TowelColorPink", map.GridCoords);
            Assert.That(bins.TryInsertIntoBin(dispenser, overflow), Is.False);
            var towels = Enumerable.ToArray(bin.Items);
            foreach (var towel in towels.Reverse())
            {
                Assert.That(bins.TryRemoveFromBin(dispenser, towel), Is.True);
                CheckLevel(bin.Items.Count == 0 ? 0 : bin.Items.Count <= 5 ? 1 : 2);
            }
            var paper = em.SpawnEntity("Paper", map.GridCoords);
            Assert.That(bins.TryInsertIntoBin(dispenser, paper), Is.False);
            Assert.That(bin.Items, Is.Empty);
            foreach (var towel in towels.Take(9))
                Assert.That(bins.TryInsertIntoBin(dispenser, towel), Is.True);
            Assert.That(bins.TryInsertIntoBin(dispenser, overflow), Is.True);
            CheckLevel(2);

            var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
            var hands = em.System<ServerHands>();
            hands.AddHand(user, "towel-test", HandLocation.Right);
            hands.SetActiveHand(user, "towel-test");
            var take = new InteractHandEvent(user, dispenser);
            em.EventBus.RaiseLocalEvent(dispenser, take);
            Assert.That(take.Handled, Is.True);
            Assert.That(hands.GetHeldItem(user, "towel-test"), Is.EqualTo(overflow));
            Assert.That(bin.Items, Has.Count.EqualTo(9));
            var verbs = new GetVerbsEvent<AlternativeVerb>(user, dispenser, overflow,
                em.GetComponent<HandsComponent>(user), true, true, true, new());
            em.EventBus.RaiseLocalEvent(dispenser, verbs);
            Assert.That(bin.Items, Has.Count.EqualTo(9), "Opening the context menu must not insert the held towel.");
            Assert.That(hands.GetHeldItem(user, "towel-test"), Is.EqualTo(overflow));
            Assert.That(em.System<SharedInteractionSystem>().InteractUsing(user, overflow, dispenser, map.GridCoords), Is.True);
            Assert.That(bin.Items.Last(), Is.EqualTo(overflow), "Returning a towel must preserve the same item.");
            Assert.That(bin.ItemContainer.Count, Is.EqualTo(10));
            Assert.That(hands.GetHeldItem(user, "towel-test"), Is.Null);
            CheckLevel(2);

            em.EventBus.RaiseLocalEvent(dispenser, new InteractHandEvent(user, dispenser));
            var insertVerbs = new GetVerbsEvent<AlternativeVerb>(user, dispenser, overflow,
                em.GetComponent<HandsComponent>(user), true, true, true, new());
            em.EventBus.RaiseLocalEvent(dispenser, insertVerbs);
            Assert.That(bin.Items, Has.Count.EqualTo(9));
            insertVerbs.Verbs.Single(verb => verb.Category == VerbCategory.Insert).Act!();
            Assert.That(bin.Items, Has.Count.EqualTo(10), "Only selecting the insertion verb may change the contents.");
            Assert.That(hands.GetHeldItem(user, "towel-test"), Is.Null);

            Assert.That(hands.TryPickup(user, towels.Last()), Is.True);
            em.System<SharedInteractionSystem>().InteractUsing(user, towels.Last(), dispenser, map.GridCoords);
            Assert.That(bin.Items, Has.Count.EqualTo(10));
            Assert.That(hands.GetHeldItem(user, "towel-test"), Is.EqualTo(towels.Last()), "A full basket must leave the towel in hand.");
            Assert.That(hands.TryDrop(user, checkActionBlocker: false, doDropInteraction: false), Is.True);
            Assert.That(hands.TryPickup(user, paper), Is.True);
            em.System<SharedInteractionSystem>().InteractUsing(user, paper, empty, map.GridCoords);
            Assert.That(em.GetComponent<BinComponent>(empty).Items, Is.Empty);
            Assert.That(hands.GetHeldItem(user, "towel-test"), Is.EqualTo(paper));

            var paperBin = em.SpawnEntity("PaperBin", map.GridCoords);
            Assert.That(em.GetComponent<BinComponent>(paperBin).InsertOnInteract, Is.False);
            Assert.That(em.System<SharedInteractionSystem>().InteractUsing(user, paper, paperBin, map.GridCoords), Is.True);
            Assert.That(em.GetComponent<BinComponent>(paperBin).Items.Single(), Is.EqualTo(paper));
        });
        TestContext.Out.WriteLine("Towel dispenser spawn, capacity, whitelist, all fill levels and hand interaction round-trip passed.");
        await pair.CleanReturnAsync();
    }
}

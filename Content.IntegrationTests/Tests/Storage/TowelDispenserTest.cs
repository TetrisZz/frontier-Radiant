using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._Triad.Shipyard;
using Content.Shared._HL.Shipyard;
using Content.Shared.Hands.Components;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Shared.Localization;
using ServerHands = Content.Server.Hands.Systems.HandsSystem;

namespace Content.IntegrationTests.Tests.Storage;

[TestFixture]
public sealed class TowelDispenserTest
{
    [Test]
    public async Task ShipSavePreservesEmptyPartialAndFullBasketsWithoutRefilling()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var em = pair.Server.EntMan;
            var loc = pair.Server.ResolveDependency<ILocalizationManager>();
            var oldCulture = loc.DefaultCulture;
            var russian = CultureInfo.GetCultureInfo("ru-RU");
            if (!loc.HasCulture(russian))
                loc.LoadCulture(russian);
            loc.DefaultCulture = russian;
            try
            {
                foreach (var (count, ending) in new[]
                         {
                             (0, "\u043e\u0432."), (1, "\u0442."), (2, "\u0430."), (5, "\u043e\u0432."),
                             (11, "\u043e\u0432."), (21, "\u0442."), (22, "\u0430."), (25, "\u043e\u0432."),
                         })
                {
                    var text = loc.GetString("bin-component-on-examine-text", ("count", count));
                    Assert.That(text, Does.StartWith("\u0412\u043d\u0443\u0442\u0440\u0438 ").And.Contain(count.ToString()).And.EndWith(ending));
                }
            }
            finally
            {
                loc.DefaultCulture = oldCulture;
            }
            var bins = em.System<BinSystem>();
            var expected = new Dictionary<string, string[]>();
            var originals = new List<EntityUid>();
            foreach (var (prototype, count) in new[]
                     {
                         ("TowelDispenser", 0), ("TowelDispenserFilled", 0),
                         ("TowelDispenserFilled", 3), ("TowelDispenserFilled", 10),
                     })
            {
                var basket = em.SpawnEntity(prototype, map.GridCoords);
                originals.Add(basket);
                var name = prototype + "-" + count;
                em.System<MetaDataSystem>().SetEntityName(basket, name);
                Assert.That(em.GetComponent<HLPersistOnShipSaveComponent>(basket).SaveContents, Is.True);
                var bin = em.GetComponent<BinComponent>(basket);
                foreach (var towel in Enumerable.Reverse(bin.Items).ToArray())
                    Assert.That(bins.TryRemoveFromBin(basket, towel), Is.True);
                for (var i = 0; i < count; i++)
                {
                    var towel = em.SpawnEntity(i % 2 == 0 ? "TowelColorPink" : "TowelColorBlue", map.GridCoords);
                    Assert.That(bins.TryInsertIntoBin(basket, towel), Is.True);
                }
                expected[name] = bin.Items.Select(item => em.GetComponent<MetaDataComponent>(item).EntityPrototype!.ID).ToArray();
            }

            Assert.That(em.System<ShipyardGridSaveSystem>().TryBuildShipSaveYaml(map.Grid.Owner, out var yaml, out _), Is.True);
            foreach (var basket in originals)
                Assert.That(em.GetComponent<BinComponent>(basket).Items, Has.Count.EqualTo(expected[em.GetComponent<MetaDataComponent>(basket).EntityName].Length));
            using var reader = new StringReader(yaml!);
            Assert.That(em.System<MapLoaderSystem>().TryLoadGrid(map.MapId, reader, "saved towel baskets",
                out var loaded, offset: new Vector2(20, 20)), Is.True);
            var query = em.EntityQueryEnumerator<BinComponent, TransformComponent>();
            var found = 0;
            while (query.MoveNext(out var uid, out var bin, out var transform))
            {
                if (transform.GridUid != loaded!.Value.Owner)
                    continue;
                found++;
                var name = em.GetComponent<MetaDataComponent>(uid).EntityName;
                Assert.That(expected.ContainsKey(name), Is.True, name);
                Assert.That(bin.ItemContainer.Count, Is.EqualTo(expected[name].Length), name);
                Assert.That(bin.Items.Select(item => em.GetComponent<MetaDataComponent>(item).EntityPrototype!.ID),
                    Is.EqualTo(expected[name]), "Loading must preserve towel colors and order without duplicates or refills: " + name);
                Assert.That(em.System<SharedAppearanceSystem>().TryGetData<int>(uid, StorageFillVisuals.FillLevel, out var level), Is.True);
                Assert.That(level, Is.EqualTo(expected[name].Length == 0 ? 0 : expected[name].Length <= 5 ? 1 : 2));
                if (bin.Items.Count > 0)
                    Assert.That(bins.TryRemoveFromBin(uid, bin.Items.Last()), Is.True);
            }
            Assert.That(found, Is.EqualTo(expected.Count));
            em.DeleteEntity(loaded!.Value.Owner);
        });
        TestContext.Out.WriteLine("Empty, partial and full towel basket ship-save round trips preserved counts, colors, order and fill visuals without refills.");
        await pair.CleanReturnAsync();
    }

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

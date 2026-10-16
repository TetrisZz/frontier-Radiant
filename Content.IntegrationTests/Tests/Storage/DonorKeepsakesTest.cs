using System;
using System.Linq;
using System.Numerics;
using Content.Server._radiant.Donor;
using Content.Server._CD.Engraving;
using Content.Server._CorvaxGoob.Photo;
using Content.Shared._CorvaxGoob.Photo;
using Content.Shared._radiant.Donor;
using Content.Shared.Clock;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Interaction;
using Content.Shared.Mind;
using Content.Shared.Storage;
using Content.Shared.Store;
using Content.Shared.Verbs;
using Robust.Client.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Storage;

[TestFixture]
public sealed class DonorKeepsakesTest
{
    private static readonly string[] Products =
    {
        "DonorPhotoMedallion", "DonorEngravedWatch", "DonorEngravedWatchSilver", "DonorBraceletPair",
    };

    [Test]
    public async Task KeepsakesLoadAndInteract()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        try
        {
            var viewers = await pair.Server.AddDummySessions(1);
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.EntMan;
                var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
                var slots = entities.System<ItemSlotsSystem>();
                var toggles = entities.System<ItemToggleSystem>();
                var keepsakes = entities.System<DonorKeepsakeSystem>();
                var user = entities.SpawnEntity("MobHuman", map.GridCoords);
                var minds = entities.System<SharedMindSystem>();
                var mind = minds.CreateMind(viewers[0].UserId);
                minds.TransferTo(mind, user);
                foreach (var id in Products)
                {
                    var product = entities.SpawnEntity(id, map.GridCoords);
                    Assert.That(prototypes.HasIndex<ListingPrototype>("UplinkHom" + id), Is.True, id);
                    if (id != "DonorBraceletPair")
                        Assert.That(entities.HasComponent<EngraveableComponent>(product), Is.True, id);
                    entities.DeleteEntity(product);
                }
                foreach (var id in new[] { "DonorInstantCamera", "DonorPhotoFrame", "DonorPersonalStamp", "DonorDisplayCase" })
                {
                    Assert.That(prototypes.HasIndex<EntityPrototype>(id), Is.False, id);
                    Assert.That(prototypes.HasIndex<ListingPrototype>("UplinkHom" + id), Is.False, id);
                }
                var photo = entities.SpawnEntity("PhotoCard", map.GridCoords);
                var paper = entities.SpawnEntity("Paper", map.GridCoords);
                var medallion = entities.SpawnEntity("DonorPhotoMedallion", map.GridCoords);
                Assert.That(slots.TryGetSlot(medallion, "photo", out var photoSlot), Is.True);
                Assert.That(photoSlot!.Locked, Is.True);
                Assert.That(toggles.TryActivate(medallion), Is.True);
                Assert.That(photoSlot.Locked, Is.False);
                Assert.That(slots.CanInsert(medallion, paper, user, photoSlot), Is.False);
                Assert.That(slots.TryInsert(medallion, "photo", photo, user), Is.True);
                var photoData = new byte[] { 1, 2, 3 };
                entities.GetComponent<PhotoCardComponent>(photo).ImageData = photoData;
                var photoVerbs = new GetVerbsEvent<AlternativeVerb>(user, medallion, null, null, true, true, true, new());
                entities.EventBus.RaiseLocalEvent(medallion, photoVerbs);
                photoVerbs.Verbs.Single(v => v.Text == Robust.Shared.Localization.Loc.GetString("donor-photo-view")).Act!();
                var interfaces = entities.System<Robust.Server.GameObjects.UserInterfaceSystem>();
                Assert.That(interfaces.TryGetUiState<PhotoCardUiState>(medallion, PhotoCardUiKey.Key, out var photoState), Is.True);
                Assert.That(photoState!.ImageData, Is.EqualTo(photoData));
                Assert.That(interfaces.IsUiOpen(medallion, PhotoCardUiKey.Key, user), Is.True);
                Assert.That(toggles.TryDeactivate(medallion), Is.True);
                Assert.That(interfaces.IsUiOpen(medallion, PhotoCardUiKey.Key, user), Is.False);
                Assert.That(slots.TryEject(medallion, "photo", user, out _), Is.False);
                Assert.That(toggles.TryActivate(medallion), Is.True);
                photoVerbs.Verbs.Single(v => v.Text == Robust.Shared.Localization.Loc.GetString("donor-photo-view")).Act!();
                Assert.That(interfaces.IsUiOpen(medallion, PhotoCardUiKey.Key, user), Is.True);
                Assert.That(slots.TryEject(medallion, "photo", user, out var extracted), Is.True);
                Assert.That(extracted, Is.EqualTo(photo));
                Assert.That(interfaces.IsUiOpen(medallion, PhotoCardUiKey.Key, user), Is.False);

                var watch = entities.SpawnEntity("DonorEngravedWatch", map.GridCoords);
                Assert.That(entities.HasComponent<ClockComponent>(watch), Is.True);
                var stopwatch = entities.GetComponent<DonorStopwatchComponent>(watch);
                keepsakes.ToggleStopwatch((watch, stopwatch));
                Assert.That(stopwatch.Started, Is.Not.Null);
                keepsakes.ToggleStopwatch((watch, stopwatch));
                Assert.That(stopwatch.Started, Is.Null);
                Assert.That(keepsakes.StopwatchElapsed((watch, stopwatch)), Is.GreaterThanOrEqualTo(TimeSpan.Zero));

                var bracelets = entities.SpawnEntity("DonorBraceletPair", map.GridCoords);
                Assert.That(entities.GetComponent<StorageComponent>(bracelets).StoredItems.Count, Is.EqualTo(2));
                var braceletItems = entities.GetComponent<StorageComponent>(bracelets).StoredItems.Keys.ToArray();
                var original = entities.GetComponent<DonorKeepsakeStyleComponent>(braceletItems[0]);
                original.Style = 1;
                var match = new InteractUsingEvent(user, braceletItems[0], braceletItems[1], map.GridCoords);
                entities.EventBus.RaiseLocalEvent(braceletItems[1], match);
                Assert.That(match.Handled, Is.True);
                var matched = entities.GetComponent<DonorKeepsakeStyleComponent>(braceletItems[1]);
                Assert.That(matched.Style, Is.EqualTo(original.Style));
                TestContext.Out.WriteLine("Donor keepsake server interaction assertions completed");
            });
            await pair.Client.WaitAssertion(() =>
            {
                var entities = pair.Client.EntMan;
                var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
                var sprites = entities.System<SpriteSystem>();
                var appearance = entities.System<SharedAppearanceSystem>();
                var mapEntity = entities.System<SharedMapSystem>().CreateMap(out var mapId);
                foreach (var id in Products)
                {
                    var uid = entities.SpawnEntity(id, new MapCoordinates(Vector2.Zero, mapId));
                    var sprite = entities.GetComponent<SpriteComponent>(uid);
                    Assert.That(sprite.BaseRSI, Is.Not.Null, id);
                    Assert.That(sprite.BaseRSI!.Size.X, Is.LessThanOrEqualTo(64), id);
                    Assert.That(sprite.BaseRSI.Size.Y, Is.LessThanOrEqualTo(64), id);
                    if (sprite.BaseRSI.Size.X == 64)
                        Assert.That(sprite.Scale, Is.EqualTo(new Vector2(id == "DonorPhotoMedallion" ? 0.25f : 0.5f)), id);
                    entities.DeleteEntity(uid);
                }
                var bracelet = entities.SpawnEntity("DonorPairedBracelet", new MapCoordinates(Vector2.Zero, mapId));
                var braceletSprite = entities.GetComponent<SpriteComponent>(bracelet);
                foreach (var (style, expected) in new[] { (0, "green"), (1, "gold") })
                {
                    appearance.SetData(bracelet, DonorKeepsakeVisuals.Style, style);
                    entities.System<AppearanceSystem>().OnChangeData(bracelet, braceletSprite);
                    Assert.That(sprites.LayerMapTryGet(bracelet, "bracelet", out var layer, false), Is.True);
                    Assert.That(braceletSprite[layer].RsiState.ToString(), Is.EqualTo(expected));
                }
                entities.DeleteEntity(bracelet);
                entities.DeleteEntity(mapEntity);
                TestContext.Out.WriteLine("Donor sprite assertions completed");
            });
            await pair.CleanReturnAsync();
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Keepsake regression failure: {exception}");
            throw;
        }
    }
}

using System;
using Content.Server._CorvaxGoob.Photo;
using Content.Server.Lathe;
using Content.Shared.Lathe;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Containers;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Storage;

[TestFixture]
public sealed class PhotoAlbumTest
{
    [Test]
    public async Task AlbumStoresOnlyTwentyOnePhotosAndIsPrintableWithoutResearch()
    {
        await using var pair = await PoolManager.GetServerClient();
        try
        {
            var map = await pair.CreateTestMap();
            await pair.Server.WaitAssertion(() =>
            {
                var entities = pair.Server.ResolveDependency<IEntityManager>();
                var storage = entities.System<SharedStorageSystem>();
                var containers = entities.System<SharedContainerSystem>();
                var album = entities.SpawnEntity("PhotoAlbum", map.GridCoords);
                var component = entities.GetComponent<StorageComponent>(album);
                var appearance = entities.System<SharedAppearanceSystem>();
                Assert.That(appearance.TryGetData<SharedBagState>(album, SharedBagOpenVisuals.BagState,
                    out var state), Is.True);
                Assert.That(state, Is.EqualTo(SharedBagState.Closed));
                var reader = entities.SpawnEntity("MobHuman", map.GridCoords);
                var ui = entities.System<SharedUserInterfaceSystem>();
                Assert.That(ui.TryOpenUi(album, StorageComponent.StorageUiKey.Key, reader), Is.True);
                Assert.That(appearance.TryGetData<SharedBagState>(album, SharedBagOpenVisuals.BagState,
                    out state), Is.True);
                Assert.That(state, Is.EqualTo(SharedBagState.Open));
                ui.CloseUi(album, StorageComponent.StorageUiKey.Key, reader);
                Assert.That(appearance.TryGetData<SharedBagState>(album, SharedBagOpenVisuals.BagState,
                    out state), Is.True);
                Assert.That(state, Is.EqualTo(SharedBagState.Closed));

                foreach (var prototype in new[] { "Paper", "Pen", "BookSpaceEncyclopedia" })
                {
                    var item = entities.SpawnEntity(prototype, map.GridCoords);
                    Assert.That(storage.CanInsert(album, item, out _), Is.False, prototype);
                    Assert.That(storage.Insert(album, item, out _, playSound: false), Is.False, prototype);
                }

                var first = EntityUid.Invalid;
                var imageData = new byte[] { 1, 2, 3, 4 };
                for (var i = 0; i < 21; i++)
                {
                    var photo = entities.SpawnEntity("PhotoCard", map.GridCoords);
                    if (i == 0)
                    {
                        first = photo;
                        entities.GetComponent<PhotoCardComponent>(photo).ImageData = imageData;
                    }
                    Assert.That(storage.Insert(album, photo, out _, playSound: false), Is.True, $"Photo {i + 1}");
                }

                var overflow = entities.SpawnEntity("PhotoCard", map.GridCoords);
                Assert.That(component.StoredItems, Has.Count.EqualTo(21));
                Assert.That(storage.Insert(album, overflow, out _, playSound: false), Is.False);
                Assert.That(containers.Remove(first, component.Container), Is.True);
                Assert.That(entities.GetComponent<PhotoCardComponent>(first).ImageData, Is.EqualTo(imageData));
                Assert.That(storage.Insert(album, overflow, out _, playSound: false), Is.True);
                Assert.That(component.StoredItems, Has.Count.EqualTo(21));

                var lathes = entities.System<LatheSystem>();
                foreach (var prototype in new[] { "ServiceTechFab", "McServiceTechFab" })
                {
                    var lathe = entities.SpawnEntity(prototype, map.GridCoords);
                    var recipes = lathes.GetAvailableRecipes(lathe, entities.GetComponent<LatheComponent>(lathe));
                    Assert.That(recipes.Exists(recipe => recipe.Id == "PhotoAlbum"), Is.True, prototype);
                }
                TestContext.Out.WriteLine("Photo album assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Photo album regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}

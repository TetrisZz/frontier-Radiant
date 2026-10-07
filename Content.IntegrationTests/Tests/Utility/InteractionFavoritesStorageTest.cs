using Content.Client.Interaction.Panel.Ui;
using Robust.Client;
using Robust.Client.ResourceManagement;
using Robust.Shared.ContentPack;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class InteractionFavoritesStorageTest
{
    [Test]
    public async Task FavoritesRoundTripThroughClientUserData()
    {
        var options = new RobustIntegrationTest.ClientIntegrationOptions
        {
            ContentStart = true,
            Options = new GameControllerOptions { LoadConfigAndUserData = false, LoadContentResources = false },
        };
        options.BeforeStart += () =>
        {
            var factory = IoCManager.Resolve<IComponentFactory>();
            factory.DoAutoRegistrations();
            factory.GenerateNetIds();
        };
        using var client = new RobustIntegrationTest.ClientIntegrationInstance(options);
        await client.WaitIdleAsync();
        await client.WaitAssertion(() =>
        {
            var data = IoCManager.Resolve<IResourceCache>().UserData;
            var path = new ResPath($"/interaction-favorites-test-{Guid.NewGuid():N}.txt");
            try
            {
                var original = new InteractionFavorites();
                original.Add("First");
                original.Add("Second");
                original.Move("Second", -1);
                using (var writer = data.OpenWriteText(path))
                    writer.Write(original.Serialize());

                Assert.That(data.TryReadAllText(path, out var stored), Is.True);
                var reopened = new InteractionFavorites();
                reopened.Load(stored!);
                Assert.That(reopened.Ids, Is.EqualTo(new[] { "Second", "First" }));
            }
            finally
            {
                if (data.Exists(path))
                    data.Delete(path);
            }
        });
    }
}

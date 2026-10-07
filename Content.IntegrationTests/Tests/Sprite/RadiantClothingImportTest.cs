using Robust.Client.GameObjects;

namespace Content.IntegrationTests.Tests.Sprite;

[TestFixture]
public sealed class RadiantClothingImportTest
{
    [Test]
    public async Task ImportedClothingAndVapeStatesExist()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        await pair.Client.WaitAssertion(() =>
        {
            var client = pair.Client;
            var sprites = client.System<SpriteSystem>();
            foreach (var id in new[]
            {
                "ClothingUniformRadiantReallyBlackSuit", "ClothingUniformRadiantBlackSuitFem",
                "ClothingUniformRadiantReallyBlackSuitSkirt", "ClothingUniformRadiantBlackSuitFemSkirt",
                "ClothingUniformRadiantParamedicDark", "ClothingUniformRadiantParamedicDarkSkirt",
                "ClothingUnderwearBottomRadiantStriped", "ClothingUnderwearBottomRadiantBee",
                "DisposableVapeBlack", "DisposableVapeBlue", "DisposableVapeGreen", "DisposableVapeOrange",
                "DisposableVapePurple", "DisposableVapeRed", "DisposableVapeWhite", "DisposableVapeYellow",
            })
            {
                var uid = client.EntMan.Spawn(id);
                var sprite = client.EntMan.GetComponent<SpriteComponent>(uid);
                Assert.That(sprite.Icon, Is.Not.Null, id);
                Assert.That(sprites.TryGetLayer((uid, sprite), 0, out var layer, false), Is.True, id);
                var rsi = layer.ActualRsi;
                Assert.That(rsi, Is.Not.Null, id);
                var slot = id.StartsWith("DisposableVape") ? "MASK"
                    : id.StartsWith("ClothingUnderwear") ? "UNDERWEARB" : "INNERCLOTHING";
                Assert.That(rsi!.TryGetState("equipped-" + slot, out _), Is.True, id);
                if (id.Contains("ParamedicDark"))
                    Assert.That(rsi.TryGetState("folded-equipped-INNERCLOTHING", out _), Is.True, id);
                if (slot == "UNDERWEARB")
                    foreach (var species in new[] { "feroxi", "vulpkanin", "tajaran", "reptilian" })
                        Assert.That(rsi.TryGetState("equipped-UNDERWEARB-" + species, out _), Is.True, id + species);
                client.EntMan.DeleteEntity(uid);
            }
        });
        await pair.CleanReturnAsync();
    }
}

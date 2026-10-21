using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using Content.Client._radiant.Fishing;
using Content.Client._radiant.Social;
using Content.Client.Stylesheets;
using Content.Shared._radiant.Fishing.Components;
using Content.Shared._radiant.Social;
using Robust.Client.GameObjects;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Timing;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class SocialUpgradesLayoutTest
{
    [Test]
    public async Task WindowsFitBothLanguagesAndDanceVisualsReset()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        try
        {
            await pair.Client.WaitAssertion(() =>
            {
                var stylesheet = new StyleNano(IoCManager.Resolve<IResourceCache>()).Stylesheet;
                var loc = IoCManager.Resolve<ILocalizationManager>();
                foreach (var locale in new[] { "ru-RU", "en-US" })
                {
                    var culture = CultureInfo.GetCultureInfo(locale);
                    if (!loc.HasCulture(culture))
                        loc.LoadCulture(culture);
                    loc.DefaultCulture = culture;
                    using var hair = new HairAccessoryWindow { Stylesheet = stylesheet };
                    hair.UpdateState(new HairAccessoryUiState(["HumanHairPonytail", "HumanHairBun3"], "HumanHairBun3"));
                    Assert.That(Descendants(hair).OfType<ItemList>().Single().Count(), Is.EqualTo(2));
                    Assert.That(Descendants(hair).OfType<Slider>().Any(), Is.False, "No color controls in the accessory picker.");
                    CheckSize(hair, new Vector2(300, 300));
                    CheckSize(hair, new Vector2(400, 460));
                    using var pole = new DancePoleWindow { Stylesheet = stylesheet };
                    pole.UpdateState(new DancePoleUiState(null, PoleDanceMove.Turn, 0, true, 1), false);
                    CheckSize(pole, new Vector2(370, 340));
                    foreach (var button in Descendants(pole).OfType<Button>().Where(b => !string.IsNullOrWhiteSpace(b.Text)))
                    {
                        button.Label.Measure(new Vector2(float.PositiveInfinity, 40));
                        Assert.That(button.Label.DesiredSize.X, Is.LessThanOrEqualTo(button.Width), button.Text);
                    }
                    using var scanner = new FishScannerWindow { Stylesheet = stylesheet };
                    scanner.UpdateState(new FishScannerUiState(new string('W', 200), true, 200, 30, 4,
                        [new FishScanEntry("Fish", "fish-scanner-female", true, true, true, "fish-scanner-alive", 0, 40, Color.White)], 75));
                    CheckSize(scanner, new Vector2(340, 320));
                    CheckSize(scanner, new Vector2(420, 470));
                }
                var em = IoCManager.Resolve<IEntityManager>();
                var uid = em.SpawnEntity(null, MapCoordinates.Nullspace);
                var sprite = em.AddComponent<SpriteComponent>(uid);
                var originalOffset = sprite.Offset;
                var originalRotation = sprite.Rotation;
                var dance = em.AddComponent<PoleDancerComponent>(uid);
                dance.Move = PoleDanceMove.Lean;
                dance.StartedAt = IoCManager.Resolve<IGameTiming>().CurTime - TimeSpan.FromSeconds(0.6);
                em.System<PoleDanceVisualizerSystem>().FrameUpdate(0.016f);
                Assert.That(sprite.Offset, Is.Not.EqualTo(originalOffset));
                Assert.That(sprite.Rotation, Is.Not.EqualTo(originalRotation));
                dance.Move = PoleDanceMove.Turn;
                em.System<PoleDanceVisualizerSystem>().FrameUpdate(0.016f);
                Assert.That(sprite.EnableDirectionOverride, Is.True);
                em.RemoveComponent<PoleDancerComponent>(uid);
                Assert.That(sprite.Offset, Is.EqualTo(originalOffset));
                Assert.That(sprite.Rotation, Is.EqualTo(originalRotation));
                Assert.That(sprite.EnableDirectionOverride, Is.False);
                em.DeleteEntity(uid);
            });
        }
        catch (Exception error)
        {
            TestContext.Out.WriteLine(error);
            throw;
        }
        TestContext.Out.WriteLine("Windows fit both languages; hairstyle thumbnails load and dance sprite offsets, rotation and direction reset.");
        await pair.CleanReturnAsync();
    }

    private static void CheckSize(Control window, Vector2 size)
    {
        window.SetSize = size;
        window.Measure(size);
        window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
        Assert.That(window.DesiredSize.X, Is.LessThanOrEqualTo(size.X));
        Assert.That(window.DesiredSize.Y, Is.LessThanOrEqualTo(size.Y));
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}

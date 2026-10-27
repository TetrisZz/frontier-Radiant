using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._radiant.Passports;
using Content.Shared._radiant.Passports;
using Content.Shared.Humanoid.Prototypes;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Preferences;
using Robust.Client.GameObjects;
using Robust.Client;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class PassportAppearanceTest
{
    [Test]
    public async Task PhotographsHideAppendagesAndFreezeAllSpeciesLayersWithoutChangingTheProfile()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            await pair.Client.WaitAssertion(() =>
            {
                var em = pair.Client.EntMan;
                foreach (var species in new[] { "Vulpkanin", "Diona", "SlimePerson" })
                {
                    var source = HumanoidCharacterProfile.DefaultWithSpecies(species).Appearance;
                    if (species == "Vulpkanin")
                        source.Markings = [new Marking("FoxTail", 3)];
                    var originalCount = source.Markings.Count;
                    var state = new PassportUiState("Test Holder", species, "Male", 30, 175,
                        "TEST-1234", "", "", "", RadiantCitizenship.Zon, source,
                        PassportPhotoKind.Portrait, null, false, false);
                    using var window = new PassportWindow();
                    window.Update(state);
                    var view = Descendants(window).OfType<Robust.Client.UserInterface.Controls.SpriteView>().Single();
                    Assert.That(view.Entity, Is.Not.Null);
                    var dummy = view.Entity!.Value.Owner;
                    Assert.That(em.HasComponent<PassportPortraitComponent>(dummy), Is.True);
                    var appearance = em.GetComponent<HumanoidAppearanceComponent>(dummy);
                    Assert.That(appearance.PermanentlyHidden, Does.Contain(HumanoidVisualLayers.Tail));
                    Assert.That(appearance.PermanentlyHidden, Does.Contain(HumanoidVisualLayers.Wings));
                    em.System<PassportPortraitSystem>().FrameUpdate(0);
                    var sprite = em.GetComponent<SpriteComponent>(dummy);
                    if (species == "Vulpkanin")
                    {
                        var tail = pair.Client.ProtoMan.Index<MarkingPrototype>("FoxTail");
                        foreach (var specifier in tail.Sprites.Cast<SpriteSpecifier.Rsi>())
                        {
                            Assert.That(em.System<SpriteSystem>().LayerMapTryGet((dummy, sprite),
                                $"FoxTail-{specifier.RsiState}", out var layer, false), Is.True);
                            Assert.That(sprite[layer].Visible, Is.False, "Hide the actual fox-tail marking, not only its base layer.");
                        }
                    }
                    foreach (var layer in sprite.AllLayers)
                    {
                        Assert.That(layer.AutoAnimated, Is.False, species);
                        Assert.That(layer.AnimationFrame, Is.Zero, species);
                    }
                    Assert.That(source.Markings.Count, Is.EqualTo(originalCount));
                }
            });
        }
        finally
        {
            await pair.CleanReturnAsync();
        }
    }

    [Test]
    public async Task WatermarksPreserveDocumentVariantsAndDoNotInterceptInput()
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
            // The bare client has no content prototypes. Register the kind so
            // the window can exercise its unknown-species text fallback.
            IoCManager.Resolve<IPrototypeManager>().RegisterKind(typeof(SpeciesPrototype));
            foreach (var citizenship in Enum.GetValues<RadiantCitizenship>())
            foreach (var temporary in new[] { false, true })
            foreach (var star in new[] { false, true })
            {
                var state = new PassportUiState("Test Holder", "Human", "Male", 30, 175,
                    "TEST-1234", "Residence", "Contact", "", citizenship, null,
                    PassportPhotoKind.Sketch, RadiantCitizenship.Aurum, star, temporary);
                using var window = new PassportWindow();
                window.Update(state);
                var size = new Vector2(930, 550);
                window.Measure(size);
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                Assert.That(window.ChildCount, Is.EqualTo(1));
                Assert.That(Descendants(window).Any(c => c.Name is "WindowHeader" or "TitleLabel"), Is.False);
                var close = Descendants(window).OfType<Robust.Client.UserInterface.Controls.TextureButton>()
                    .Single(c => c.Name == "CloseButton");
                Assert.That(close.Disabled, Is.False);
                Assert.That(close.GlobalPosition.X + close.Size.X, Is.GreaterThan(window.GlobalPosition.X + size.X - 60));
                var cover = Descendants(window).Single(c => c.Name == "PassportCover");
                Assert.That(cover.Size.X, Is.EqualTo(size.X));
                Assert.That(cover.GlobalPosition.Y, Is.GreaterThanOrEqualTo(close.GlobalPosition.Y + close.Size.Y));
                Assert.That(cover.GlobalPosition.Y + cover.Size.Y, Is.EqualTo(window.GlobalPosition.Y + size.Y));
                var seal = (PassportSeal) Descendants(window).Single(c => c.Name == "PassportWatermark");
                Assert.That(seal.Size.X, Is.EqualTo(270).Within(1));
                Assert.That(seal.Size.Y, Is.EqualTo(270).Within(1));
                Assert.That(seal.InkColor.A, Is.EqualTo(0.18f).Within(0.001f));
                Assert.That(seal.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Ignore));
                Assert.That(seal.Parent!.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Ignore));
                Assert.That(seal.Parent!.Parent!.Children.First(), Is.SameAs(seal.Parent),
                    "The watermark must be drawn behind the document contents.");
                Assert.That(Descendants(window).OfType<Robust.Client.UserInterface.Controls.Button>(), Is.Empty,
                    "The document must not have page-switching buttons.");
                window.Update(state);
                Assert.That(Descendants(window).Count(c => c.Name == "PassportWatermark"), Is.EqualTo(1));
                Assert.That(Descendants(window).Count(c => c.Name == "CloseButton"), Is.EqualTo(1));
                var closed = false;
                window.OnClose += () => closed = true;
                window.OpenCentered();
                Assert.That(window.IsOpen, Is.True);
                window.Close();
                Assert.That(closed, Is.True);
                Assert.That(window.IsOpen, Is.False);
            }
        });
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

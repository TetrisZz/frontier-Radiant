using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._radiant.Passports;
using Content.Shared._radiant.Passports;
using Content.Shared.Humanoid.Prototypes;
using Robust.Client;
using Robust.Client.UserInterface;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class PassportAppearanceTest
{
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

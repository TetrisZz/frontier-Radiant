using System.Linq;
using System.Numerics;
using Content.Client._radiant.Passports;
using Content.Shared._radiant.Passports;
using Content.Shared.Humanoid.Prototypes;
using Robust.Client;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class ServiceCredentialAppearanceTest
{
    [Test]
    public async Task BothServicesHaveReadablePagesAndNonInteractiveSeals()
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
            IoCManager.Resolve<IPrototypeManager>().RegisterKind(typeof(SpeciesPrototype));
            foreach (var service in Enum.GetValues<CredentialService>())
            {
                using var window = new ServiceCredentialWindow();
                var state = new ServiceCredentialUiState(service, "", "Test Holder", "DVB-12345678",
                    "Human", "Male", 35, null, "");
                window.Update(state);
                var size = new Vector2(880, 480);
                window.Measure(size);
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                Assert.That(window.MinSize.X, Is.EqualTo(780));
                Assert.That(window.ChildCount, Is.EqualTo(1));
                Assert.That(Descendants(window).Any(c => c.Name is "WindowHeader" or "TitleLabel" or "CloseButton"), Is.False);
                var cover = Descendants(window).Single(c => c.Name == "CredentialCover");
                Assert.That(cover.GlobalPosition, Is.EqualTo(window.GlobalPosition));
                Assert.That(cover.Size, Is.EqualTo(size));
                var photo = Descendants(window).Single(c => c.Name == "CredentialPhoto");
                Assert.That(photo.Size.X, Is.EqualTo(214).Within(1));
                Assert.That(Descendants(photo).Any(c => c is PassportSketchPortrait), Is.False);
                Assert.That(Descendants(photo).Any(c => c.Name == "EmptyCredentialPhoto"), Is.True);
                var seal = Descendants(window).Single(c => c.Name == "CredentialSeal");
                Assert.That(seal.MouseFilter, Is.EqualTo(Control.MouseFilterMode.Ignore));
                Assert.That(seal, Is.TypeOf<ServiceCredentialSeal>());
                Assert.That(seal.Visible, Is.True);
                Assert.That(Descendants(window).OfType<ScrollContainer>().Count(), Is.EqualTo(2));
                window.Update(state);
                Assert.That(Descendants(window).Count(c => c.Name == "CredentialSeal"), Is.EqualTo(1));
                window.Update(new ServiceCredentialUiState(service, "", "", "", "", "", 0, null, ""));
                Assert.That(Descendants(window).Single(c => c.Name == "CredentialSeal").Visible, Is.False);
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

    private static System.Collections.Generic.IEnumerable<Control> Descendants(Control control)
    {
        foreach (var child in control.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child))
                yield return descendant;
        }
    }
}

using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Client._radiant.Fishing;
using Content.Client.Stylesheets;
using Content.Shared._radiant.Fishing.Components;
using Robust.Client;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class FishScannerLayoutTest
{
    [Test]
    public async Task SingleTopAlignedResultHasNoTabsOrChargeAndOnlyRelevantFishFields()
    {
        var resources = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
        var options = new RobustIntegrationTest.ClientIntegrationOptions
        {
            ContentStart = true,
            Options = new GameControllerOptions
            {
                LoadConfigAndUserData = false,
                LoadContentResources = false,
                PrototypeDirectory = new ResPath("/FishScannerLayoutTestPrototypes"),
                MountOptions = new MountOptions { DirMounts = new() { resources } },
            },
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
            var prototypes = IoCManager.Resolve<IPrototypeManager>();
            prototypes.LoadString(File.ReadAllText(Path.Combine(resources, "Prototypes/fonts.yml")));
            prototypes.ResolveResults();
            var loc = IoCManager.Resolve<ILocalizationManager>();
            foreach (var locale in new[] { "ru-RU", "en-US" })
            {
                var culture = CultureInfo.GetCultureInfo(locale);
                if (!loc.HasCulture(culture))
                    loc.LoadCulture(culture);
                loc.DefaultCulture = culture;
                using var window = new FishScannerWindow
                {
                    Stylesheet = new StyleNano(IoCManager.Resolve<IResourceCache>()).Stylesheet,
                };
                var unknown = new FishScanEntry("Fish", "fish-scanner-not-breedable", false, false, true,
                    "fish-scanner-alive", 0, 100, Color.White);
                window.UpdateState(new FishScannerUiState(new string('W', 200), false, 0, 0, 0, [unknown], 44));
                Assert.That(Descendants(window).OfType<TabContainer>(), Is.Empty);
                Assert.That(Descendants(window).OfType<Label>().Any(label => label.Text?.Contains("44%") == true), Is.False);
                var results = Descendants(window).Single(control => control.Name == "FishScannerResults");
                Assert.That(results.VerticalAlignment, Is.EqualTo(Control.VAlignment.Top));
                Assert.That(Descendants(window).Count(control => control.Name == "FishScannerField-health"), Is.EqualTo(1));
                Assert.That(Descendants(window).Any(control => control.Name is "FishScannerField-age" or "FishScannerField-sex" or "FishScannerField-growth"), Is.False);
                CheckSize(window, results);
                var adult = unknown with { Breedable = true, Sex = "fish-scanner-female" };
                var juvenile = adult with { Juvenile = true, Growth = 40 };
                window.UpdateState(new FishScannerUiState("Aquarium", true, 200, 30, 4, [adult, juvenile], 44));
                Assert.That(Descendants(window).Count(control => control.Name == "FishScannerField-health"), Is.EqualTo(2));
                Assert.That(Descendants(window).Count(control => control.Name == "FishScannerField-growth"), Is.EqualTo(1));
                CheckSize(window, results);
                window.UpdateState(new FishScannerUiState("", false, 0, 0, 0, [], 0));
                Assert.That(results.ChildCount, Is.EqualTo(1));
            }
        });
    }

    private static void CheckSize(FishScannerWindow window, Control results)
    {
        foreach (var size in new[] { new Vector2(340, 320), new Vector2(420, 380) })
        {
            window.SetSize = size;
            window.Measure(size);
            window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            Assert.That(window.DesiredSize.X, Is.LessThanOrEqualTo(size.X));
            Assert.That(window.DesiredSize.Y, Is.LessThanOrEqualTo(size.Y));
            Assert.That(results.GlobalPosition.Y, Is.LessThan(85), "Results must start at the top, not in the vertical center.");
            foreach (var row in results.Children.Where(control => control.Name?.StartsWith("FishScannerField-") == true))
            foreach (var column in row.Children)
                Assert.That(column.GlobalPosition.X + column.Width, Is.LessThanOrEqualTo(size.X));
        }
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

using System;
using System.Globalization;
using System.IO;
using Content.Client.Stylesheets;
using Robust.Client;
using Robust.Client.ResourceManagement;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Utility;
using Robust.UnitTesting;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using Content.Client._NF.Shipyard.UI;
using Content.Client.Guidebook;
using Content.Client.Guidebook.Richtext;
using Content.Shared.Guidebook;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.Input;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Guidebook;

public sealed class RadiantGuidebookTest
{
    [Test]
    public async Task DocumentsRenderAndNavigationResolves()
    {
        // Parsing needs a loaded client, not a connection or a full prototype-name cache from the server.
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var client = pair.Client;
        var prototypes = client.ResolveDependency<IPrototypeManager>();
        var resources = client.ResolveDependency<IResourceManager>();
        var localization = client.ResolveDependency<ILocalizationManager>();
        var parser = client.ResolveDependency<DocumentParsingManager>();
        var entries = prototypes.EnumeratePrototypes<GuideEntryPrototype>().ToDictionary(p => p.ID);
        var documents = entries.Values.Where(p => p.ID.StartsWith("Radiant") &&
            p.Text.ToString().StartsWith("/ServerInfo/_radiant/Guidebook/")).ToList();
        Assert.That(documents, Is.Not.Empty);


        foreach (var entry in documents)
        {
            await client.WaitAssertion(() =>
            {
                Assert.That(localization.HasString(entry.Name), Is.True, $"Missing guide title: {entry.ID}");
                using var reader = resources.ContentFileReadText(entry.Text);
                var text = reader.ReadToEnd();
                foreach (Match link in Regex.Matches(text, "\\blink=\"([^\"]+)\""))
                    Assert.That(entries.ContainsKey(link.Groups[1].Value), Is.True,
                        $"{entry.ID}: unknown linked guide {link.Groups[1].Value}");

                using var document = new Document();
                Assert.That(parser.TryAddMarkup(document, text), Is.True, $"Invalid guide: {entry.ID}");
                var controls = Descendants(document).ToList();
                foreach (Match heading in Regex.Matches(text, @"(?m)^#{1,3} (.+)\r?$"))
                    Assert.That(controls.OfType<Label>().Any(label => label.Text?.Trim() == heading.Groups[1].Value.Trim()),
                        Is.True, $"{entry.ID}: heading was swallowed into a paragraph: {heading.Groups[1].Value}");
                var steps = Regex.Matches(text, @"(?m)^\d+\. ").Count;
                Assert.That(controls.OfType<BoxContainer>().Count(row => row.ChildCount == 2 &&
                    row.Children.First() is Label label && Regex.IsMatch(label.Text ?? "", @"^\d+\.$")),
                    Is.GreaterThanOrEqualTo(steps), $"{entry.ID}: numbered steps must render as separate rows");
                foreach (var width in new[] { 520f, 800f })
                {
                    document.Measure(new Vector2(width, float.PositiveInfinity));
                    Assert.That(document.DesiredSize.X, Is.LessThanOrEqualTo(width + 1),
                        $"{entry.ID}: contents overflow the guide at width {width}");
                    Assert.That(float.IsFinite(document.DesiredSize.Y), Is.True, entry.ID);
                }
            });
            // Entity embeds need a tick between pages to finish their style updates.
            await client.WaitRunTicks(1);
        }

        await pair.CleanReturnAsync();
    }

    [TestCase("ru-RU")]
    [TestCase("en-US")]
    public async Task ShipSavingQuestionMarkOpensTheGuideWithoutADeed(string locale)
    {
        var resources = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
        var options = new RobustIntegrationTest.ClientIntegrationOptions
        {
            ContentStart = true,
            Options = new GameControllerOptions
            {
                LoadConfigAndUserData = false,
                LoadContentResources = false,
                PrototypeDirectory = new ResPath("/ShipSavingHelpTestPrototypes"),
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
            var protoManager = IoCManager.Resolve<IPrototypeManager>();
            protoManager.LoadString(File.ReadAllText(Path.Combine(resources, "Prototypes/fonts.yml")));
            protoManager.LoadString(File.ReadAllText(Path.Combine(resources, "Prototypes/themes.yml")));
            protoManager.ResolveResults();
            IoCManager.Resolve<IUserInterfaceManager>().SetDefaultTheme("SS14DefaultTheme");
            var loc = IoCManager.Resolve<ILocalizationManager>();
            var culture = CultureInfo.GetCultureInfo(locale);
            if (!loc.HasCulture(culture)) loc.LoadCulture(culture);
            loc.DefaultCulture = culture;
            var notification = loc.GetString("shipyard-save-traffic-announcement", ("ship", "Test shuttle"), ("owner", "Captain"));
            Assert.That(notification, Does.Not.StartWith("Диспетчерская служба:").And.Not.StartWith("Traffic control:"));
            using var menu = new ShipyardConsoleMenu
            {
                Stylesheet = new StyleNano(IoCManager.Resolve<IResourceCache>()).Stylesheet,
            };
            menu.OpenCentered();
            var size = new Vector2(640, 650);
            menu.Measure(size);
            menu.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
            var save = menu.FindControl<BaseButton>("SaveShipButton");
            var help = menu.FindControl<Button>("ShipSavingHelpButton");
            var load = menu.FindControl<BaseButton>("LoadShipButton");
            Assert.That(save.Disabled, Is.True);
            Assert.That(help.Disabled, Is.False, "Players need help even before they have a valid deed.");
            Assert.That(save.GlobalPixelPosition.X, Is.GreaterThanOrEqualTo(help.GlobalPixelPosition.X + help.PixelSize.X));
            Assert.That(load.GlobalPixelPosition.X, Is.GreaterThanOrEqualTo(save.GlobalPixelPosition.X + save.PixelSize.X));

            // Exercise the real UI and guide event without starting a world or unrelated entity systems.
            var guidebook = new GuidebookSystem();
            var systems = new Moq.Mock<IEntitySystemManager>();
            systems.Setup(manager => manager.GetEntitySystem<GuidebookSystem>()).Returns(guidebook);
            typeof(ShipyardConsoleMenu).GetField("_entitySystemManager", BindingFlags.Instance | BindingFlags.NonPublic)!
                .SetValue(menu, systems.Object);
            ProtoId<GuideEntryPrototype>? selectedGuide = null;
            void Opened(List<ProtoId<GuideEntryPrototype>> guides, List<ProtoId<GuideEntryPrototype>>? roots,
                ProtoId<GuideEntryPrototype>? forcedRoot, bool includeChildren, ProtoId<GuideEntryPrototype>? selected)
            {
                selectedGuide = selected;
                Assert.That(guides.Any(id => id.Id == "RadiantMechanics"), Is.True,
                    "Related guide links must remain available when opened from the shipyard.");
            }

            guidebook.OnGuidebookOpen += Opened;
            try
            {
                Click(help);
                Assert.That(selectedGuide?.Id, Is.EqualTo("RadiantShipSaving"));
            }
            finally
            {
                guidebook.OnGuidebookOpen -= Opened;
            }

        });
    }

    private static void Click(BaseButton button)
    {
        button.MuteSounds = true;
        var pointer = new ScreenCoordinates(button.GlobalPixelPosition + button.PixelSize / 2, button.Window?.Id ?? default);
        foreach (var (method, state) in new[] { ("KeyBindDown", BoundKeyState.Down), ("KeyBindUp", BoundKeyState.Up) })
        {
            var args = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, pointer, default,
                button.Size / 2, button.PixelSize / 2);
            typeof(BaseButton).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { args });
        }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}

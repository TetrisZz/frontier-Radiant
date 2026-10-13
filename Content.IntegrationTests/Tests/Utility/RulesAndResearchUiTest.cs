using System;
using System.Linq;
using System.Collections.Generic;
using System.Numerics;
using Content.Client.Guidebook;
using Content.Client._NF.Research.UI;
using Content.Shared._NF.Research;
using Content.Shared.Research.Prototypes;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.ContentPack;
using Robust.Shared.Prototypes;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class RulesAndResearchUiTest
{
    [Test]
    public async Task RulesKeepLinksAndResearchAcceptsNonZeroFirstTechnology()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        try
        {
            await pair.Client.WaitAssertion(() =>
            {
                var parser = pair.Client.ResolveDependency<DocumentParsingManager>();
                var resources = pair.Client.ResolveDependency<IResourceManager>();
                using var document = resources.ContentFileReadText(new ResPath("/ServerInfo/_NF/Guidebook/Rules/FrontierRuleset.xml"));
                using var rules = new BoxContainer();
                Assert.That(parser.TryAddMarkup(rules, document.ReadToEnd()), Is.True);
                var links = Descendants(rules).OfType<RichTextLabel>()
                    .SelectMany(label => label.Controls).OfType<Label>().ToArray();
                Assert.That(links, Has.Length.EqualTo(13), "Every rule must have a visible link");
                Assert.That(links.All(label => !string.IsNullOrWhiteSpace(label.Text)), Is.True);
                Assert.That(links.All(label => label.MouseFilter == Control.MouseFilterMode.Stop), Is.True);

                var prototypes = pair.Client.ResolveDependency<IPrototypeManager>();
                var technologies = prototypes.EnumeratePrototypes<TechnologyPrototype>().ToArray();
                var first = technologies.First(technology => technology.Position.X > 0 && technology.Position.Y > 0);
                using var menu = new FancyResearchConsoleMenu();
                menu.UpdatePanels(new() { [first.ID] = default });
                Assert.That(menu.FindControl<LayoutContainer>("DragContainer").Children
                    .OfType<FancyResearchConsoleItem>().Count(), Is.EqualTo(1));
                menu.UpdatePanels(technologies.ToDictionary(technology => technology.ID, _ => default(ResearchAvailability)));
                var items = menu.FindControl<LayoutContainer>("DragContainer").Children
                    .OfType<FancyResearchConsoleItem>().ToArray();
                Assert.That(items, Has.Length.EqualTo(technologies.Length));
                menu.UpdatePanels(new());
                Assert.That(menu.FindControl<LayoutContainer>("DragContainer").Children, Is.Empty);
                menu.UpdatePanels(new() { [first.ID] = default });
                menu.Measure(new Vector2(1000, 740));
                menu.Arrange(UIBox2.FromDimensions(Vector2.Zero, new Vector2(1000, 740)));
                TestContext.Out.WriteLine("Rules and research UI assertions completed");
            });
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Rules and research UI regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
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

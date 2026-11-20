using System.Globalization;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Client._radiant.Skills;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.Systems.Character.Windows;
using Content.Shared._radiant.Skills;
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

public sealed class CharacterSkillsLayoutTest
{
    [Test]
    public async Task SelectedSkillsAndDescriptionsFitTheCharacterWindow()
    {
        var resources = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
        var options = new RobustIntegrationTest.ClientIntegrationOptions
        {
            ContentStart = true,
            Options = new GameControllerOptions
            {
                LoadConfigAndUserData = false,
                LoadContentResources = false,
                PrototypeDirectory = new ResPath("/CharacterSkillsLayoutTestPrototypes"),
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

                using var window = new CharacterWindow
                {
                    Stylesheet = new StyleNano(IoCManager.Resolve<IResourceCache>()).Stylesheet,
                };
                // Include a long level-three description and leave unselected skills at zero.
                var levels = new int[ProfessionalSkillRules.Count];
                levels[(int) ProfessionalSkill.Medicine] = 3;
                levels[(int) ProfessionalSkill.Shooting] = 2;
                levels[(int) ProfessionalSkill.Engineering] = 1;
                levels[(int) ProfessionalSkill.Piloting] = 2;
                levels[(int) ProfessionalSkill.Botany] = 2;
                var skills = new CharacterSkillsControl(levels);
                window.Objectives.AddChild(new Button { Text = "Profession" });
                window.Objectives.AddChild(skills);

                var cards = skills.Children.OfType<PanelContainer>().ToArray();
                var rows = cards.Select(card => card.Children.OfType<Collapsible>().Single()).ToArray();
                Assert.That(rows, Has.Length.EqualTo(5));
                var chosen = new[] { ProfessionalSkill.Piloting, ProfessionalSkill.Shooting, ProfessionalSkill.Medicine,
                    ProfessionalSkill.Engineering, ProfessionalSkill.Botany };
                for (var i = 0; i < chosen.Length; i++)
                {
                    var key = ProfessionalSkillRules.NameKey(chosen[i]);
                    var level = levels[(int) chosen[i]];
                    Assert.That(rows[i].BodyVisible, Is.False);
                    Assert.That(rows[i].Body!.Visible, Is.False);
                    Assert.That(((CollapsibleHeading) rows[i].Heading!).Title, Is.EqualTo(Loc.GetString(key)));
                    Assert.That(Descendants(rows[i]).OfType<Label>().Single(label => label.Name == "SkillLevel").Text,
                        Is.EqualTo(Loc.GetString("professional-skills-character-level", ("level", level))));
                    Assert.That(Descendants(rows[i]).OfType<RichTextLabel>().Single().Text,
                        Is.EqualTo(Loc.GetString($"{key}-level-{level}")));
                }

                foreach (var expanded in new[] { false, true })
                foreach (var size in new[] { new Vector2(400, 545), new Vector2(520, 650) })
                {
                    foreach (var row in rows)
                        row.BodyVisible = expanded;
                    window.SetSize = size;
                    window.Measure(size);
                    window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    Assert.That(window.DesiredSize.X, Is.LessThanOrEqualTo(size.X));
                    if (!expanded)
                        Assert.That(skills.Height, Is.LessThan(400), "Five skills should fit without a wall of descriptions.");
                    foreach (var card in cards)
                        Assert.That(card.GlobalPosition.X + card.Width, Is.LessThanOrEqualTo(size.X));
                    if (!expanded)
                        continue;
                    foreach (var label in rows.SelectMany(Descendants).OfType<RichTextLabel>())
                    {
                        Assert.That(label.Width, Is.GreaterThan(0));
                        Assert.That(label.GlobalPosition.X + label.Width, Is.LessThanOrEqualTo(size.X));
                    }
                }

                using var empty = new CharacterSkillsControl(new int[ProfessionalSkillRules.Count]);
                Assert.That(empty.Children.OfType<PanelContainer>(), Is.Empty);
                Assert.That(empty.Children.OfType<RichTextLabel>().Last().Text,
                    Is.EqualTo(Loc.GetString("professional-skills-character-empty")));
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

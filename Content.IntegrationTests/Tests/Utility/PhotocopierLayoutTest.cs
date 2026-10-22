using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Content.Client._radiant.Paperwork;
using Content.Client.Paper.UI;
using Content.Client.Stylesheets;
using Content.Client.UserInterface.RichText;
using Content.Shared.Paper;
using Content.Shared._radiant.Paperwork;
using Robust.Client;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Client.UserInterface.RichText;
using Robust.Shared;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Localization;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;
using Robust.UnitTesting;
using YamlDotNet.RepresentationModel;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class PhotocopierLayoutTest
{
    [Test]
    public async Task EveryTemplateLoadsAndTheWindowFitsWithoutClippedFormNames()
    {
        var resources = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
        var options = new RobustIntegrationTest.ClientIntegrationOptions
        {
            ContentStart = true,
            Options = new GameControllerOptions
            {
                LoadConfigAndUserData = false,
                LoadContentResources = false,
                PrototypeDirectory = new ResPath("/PhotocopierLayoutTestPrototypes"),
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
            var prototypesManager = IoCManager.Resolve<IPrototypeManager>();
            prototypesManager.LoadString(File.ReadAllText(Path.Combine(resources, "Prototypes/fonts.yml")));
            prototypesManager.LoadString(File.ReadAllText(Path.Combine(resources, "Prototypes/themes.yml")));
            prototypesManager.ResolveResults();
            IoCManager.Resolve<IUserInterfaceManager>().SetDefaultTheme("SS14DefaultTheme");
            var copierSprite = IoCManager.Resolve<IResourceCache>().GetResource<RSIResource>(
                new ResPath("/Textures/_radiant/Structures/Machines/photocopier.rsi")).RSI;
            Assert.That(copierSprite.TryGetState("photocopier1", out var printing), Is.True);
            Assert.That(printing!.IsAnimated, Is.True);
            Assert.That(printing.DelayCount, Is.EqualTo(40));
            Assert.That(printing.AnimationLength, Is.EqualTo(2).Within(0.0001));
            Assert.That(copierSprite.TryGetState("photocopier", out var idle), Is.True);
            Assert.That(idle!.IsAnimated, Is.False);
            // Register the production handlers without starting the rest of the content modules.
            var tagManager = IoCManager.Resolve<MarkupTagManager>();
            var tags = (Dictionary<string, IMarkupTagHandler>) typeof(MarkupTagManager)
                .GetField("_markupTagTypes", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(tagManager)!;
            foreach (var handler in new IMarkupTagHandler[] { new FormTagHandler(), new MonoTag() })
            {
                IoCManager.InjectDependencies(handler);
                tags[handler.Name] = handler;
            }
            var loc = IoCManager.Resolve<ILocalizationManager>();
            var yaml = new YamlStream();
            using var reader = File.OpenText(Path.Combine(resources, "Prototypes/_radiant/Paperwork/forms.yml"));
            yaml.Load(reader);
            var prototypes = ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>().ToArray();
            var copierYaml = new YamlStream();
            using var copierReader = File.OpenText(Path.Combine(resources, "Prototypes/_radiant/Entities/Structures/Machines/photocopier.yml"));
            copierYaml.Load(copierReader);
            var copier = ((YamlSequenceNode) copierYaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>()
                .Single(node => node.Children[new YamlScalarNode("id")].ToString() == "Photocopier");
            var configuration = ((YamlSequenceNode) copier.Children[new YamlScalarNode("components")]).Children.Cast<YamlMappingNode>()
                .Single(node => node.Children[new YamlScalarNode("type")].ToString() == "Photocopier");
            var configuredForms = ((YamlSequenceNode) configuration.Children[new YamlScalarNode("forms")]).Children
                .Select(node => node.ToString()).ToHashSet();
            foreach (var locale in new[] { "ru-RU", "en-US" })
            {
                var culture = CultureInfo.GetCultureInfo(locale);
                if (!loc.HasCulture(culture))
                    loc.LoadCulture(culture);
                loc.DefaultCulture = culture;
                var header = loc.GetString("photocopier-document-header", ("title", "Test form"),
                    ("date", "21.10.3026"), ("time", "01:02:03"), ("ship", "Test shuttle"));
                Assert.That(header, Does.Contain("21.10.3026").And.Contain("01:02:03").And.Contain("Test shuttle"), locale);
                var forms = new List<DocumentFormEntry>();
                var frontierLetterhead = loc.GetString("document-letterhead-frontier", ("ship", "XX-000"));
                var logoLines = FormattedMessage.RemoveMarkupPermissive(frontierLetterhead).Split('\n');
                var expectedLogo = new[] { "████░░░░████", "████░░░████░", "████░░████░░", "████░████░░░", "████░░████░░", "████░░░████░", "████░░░░████" };
                Assert.That(logoLines.Length, Is.EqualTo(expectedLogo.Length));
                for (var i = 0; i < expectedLogo.Length; i++)
                    Assert.That(logoLines[i], Does.StartWith(expectedLogo[i]), "The seven-row K must not regress to the old N logo.");
                Assert.That(loc.GetString("document-letterhead-acorp", ("base", "МНБ Икарус")), Does.Contain("МНБ Икарус").And.Not.Contain("Горизонт"));
                string Value(YamlMappingNode node, string key) => ((YamlScalarNode) node.Children[new YamlScalarNode(key)]).Value!;
                string Translate(YamlMappingNode node, string field)
                {
                    var key = Value(node, field);
                    Assert.That(loc.TryGetString(key, out var value, ("ship", "Test shuttle"), ("base", "МНБ Икарус")), Is.True, locale + ": " + key);
                    Assert.That(value, Is.Not.Empty.And.Not.EqualTo(key));
                    return value!;
                }
                foreach (var prototype in prototypes)
                {
                    var title = Translate(prototype, "name");
                    var body = Translate(prototype, "content");
                    var letterhead = Translate(prototype, "letterhead");
                    Assert.That(body, Does.Contain("[form]").And.Not.Contain("___"));
                    Assert.That(letterhead, Does.Contain("[head=3]").And.Contain("[color=").And.Not.Contain("\u2605"));
                    if (configuredForms.Contains(Value(prototype, "id")))
                        forms.Add(new(Value(prototype, "id"), title, Translate(prototype, "category"),
                            letterhead + "\n\n[head=2]" + title + "[/head]\n\n" + body));
                }
                var theme = new StyleNano(IoCManager.Resolve<IResourceCache>()).Stylesheet;
                using var window = new PhotocopierWindow { Stylesheet = theme };
                window.Update(new PhotocopierUiState(forms, 20, 100, true, true));
                foreach (var size in new[] { new Vector2(720, 440), new Vector2(940, 640) })
                {
                    window.SetSize = size;
                    window.Measure(size);
                    window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    Assert.That(window.DesiredSize.X, Is.LessThanOrEqualTo(size.X));
                    Assert.That(window.DesiredSize.Y, Is.LessThanOrEqualTo(size.Y));
                    var buttons = Descendants(window).OfType<BaseButton>().Where(b => b.Name == "PhotocopierForm").ToArray();
                    Assert.That(buttons.Length, Is.EqualTo(configuredForms.Count));
                    foreach (var button in buttons)
                    {
                        var label = Descendants(button).OfType<RichTextLabel>().Single();
                        Assert.That(label.DesiredSize.X, Is.LessThanOrEqualTo(button.Size.X));
                        Assert.That(label.Size.Y + label.Margin.SumVertical, Is.GreaterThanOrEqualTo(label.DesiredSize.Y - 1));
                    }
                    foreach (var button in buttons)
                    {
                        Click(button);
                        var fields = Descendants(window).OfType<PaperFormButton>().ToArray();
                        Assert.That(fields, Is.Not.Empty, button.ToolTip + ": " +
                            Descendants(window).OfType<RichTextLabel>().Single(label => label.Name == "PhotocopierPreview").Text);
                        Assert.That(fields.All(field => field.Disabled), Is.True, "Preview fields are not editable.");
                    }
                    window.Measure(size);
                    window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                    var preview = Descendants(window).OfType<RichTextLabel>().Single(c => c.Name == "PhotocopierPreview");
                    Assert.That(preview.VerticalAlignment, Is.EqualTo(Control.VAlignment.Top));
                    Assert.That(preview.GlobalPosition.Y, Is.LessThan(110));
                    foreach (var action in Descendants(window).OfType<BaseButton>().Where(b => b.Name is "PhotocopierPrint" or "PhotocopierCopy"))
                        Assert.That(action.GlobalPosition.X + action.Size.X, Is.LessThanOrEqualTo(size.X));
                }
                window.Update(new PhotocopierUiState([], 20, 100, true, true));
                Assert.That(Descendants(window).OfType<BaseButton>().Any(button => button.Name == "PhotocopierForm"), Is.False);
                Assert.That(Descendants(window).OfType<BaseButton>().Single(button => button.Name == "PhotocopierPrint").Disabled, Is.True);
                Assert.That(Descendants(window).OfType<BaseButton>().Single(button => button.Name == "PhotocopierCopy").Disabled, Is.False, "An empty catalog still permits copying originals.");
                CheckPaperFields(theme, locale);
            }
        });
    }

    private static void CheckPaperFields(Stylesheet theme, string locale)
    {
        using var other = new PaperWindow { Stylesheet = theme };
        var root = IoCManager.Resolve<IUserInterfaceManager>().StateRoot;
        root.AddChild(other);
        other.Populate(new PaperComponent.PaperBoundUserInterfaceState("Other: [form]", []));
        using var paper = new PaperWindow { Stylesheet = theme };
        root.AddChild(paper);
        const string original = "Name: [form]\nPosition: [form]\nContents: [form]";
        paper.Populate(new PaperComponent.PaperBoundUserInterfaceState(original, []));
        var size = new Vector2(510, 660);
        paper.Measure(size);
        paper.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
        var fields = Descendants(paper).OfType<PaperFormButton>().ToArray();
        Assert.That(fields.Length, Is.EqualTo(3));
        foreach (var field in fields)
        {
            Assert.That(field.Text, Is.EqualTo(locale == "ru-RU" ? "Заполнить" : "Fill"));
            Assert.That(field.Label.DesiredSize.X, Is.LessThanOrEqualTo(field.Width));
            Assert.That(field.Width, Is.GreaterThan(32));
            Assert.That(field.Height, Is.InRange(22, 32));
        }
        string? saved = null;
        paper.OnSaved += text => saved = text;
        Assert.That(fields.All(field => field.Disabled), Is.True, "Reading without a pen must not enable fields.");
        paper.BeginFormEntry(1);
        Assert.That(Descendants(paper).OfType<LineEdit>().Any(edit => edit.Visible), Is.False);
        var heldPen = true;
        paper.CanFillForms = () => heldPen;
        paper.Populate(new PaperComponent.PaperBoundUserInterfaceState(original, [], PaperComponent.PaperAction.Write));
        Assert.That(paper.Input.VisibleInTree, Is.False, "Forms remain formatted even when opened using a pen.");
        fields = Descendants(paper).OfType<PaperFormButton>().ToArray();
        Click(fields[1]);
        Assert.That(Descendants(paper).OfType<Popup>(), Is.Empty, "Field entry must not open a separate interface.");
        var editor = Descendants(paper).OfType<LineEdit>().Single(edit => edit.Visible);
        editor.Text = "Captain";
        paper.Populate(new PaperComponent.PaperBoundUserInterfaceState(original, []));
        Assert.That(editor.Visible, Is.True, "Opening the same paper for reading must not discard a draft.");
        Assert.That(editor.Text, Is.EqualTo("Captain"));
        editor.ForceSubmitText();
        Assert.That(saved, Is.EqualTo("Name: [form]\nPosition: Captain\nContents: [form]"), "The second field must not overwrite the first.");
        paper.Populate(new PaperComponent.PaperBoundUserInterfaceState(saved!, []));
        paper.Measure(size);
        paper.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
        fields = Descendants(paper).OfType<PaperFormButton>().ToArray();
        Click(fields[1]);
        editor = Descendants(paper).OfType<LineEdit>().Single(edit => edit.Visible);
        editor.Text = "Report";
        heldPen = false;
        editor.ForceSubmitText();
        Assert.That(saved, Is.EqualTo("Name: [form]\nPosition: Captain\nContents: [form]"), "Putting the pen away before submitting must prevent saving.");
        heldPen = true;
        Click(fields[1]);
        editor.Text = "Report";
        editor.ReleaseKeyboardFocus();
        Assert.That(editor.Visible, Is.False, "Leaving the field cancels editing.");
        Click(fields[1]);
        editor.Text = "Report";
        editor.ForceSubmitText();
        Assert.That(saved, Is.EqualTo("Name: [form]\nPosition: Captain\nContents: Report"));
        paper.Populate(new PaperComponent.PaperBoundUserInterfaceState(original, []));
        paper.BeginFormEntry(0);
        editor = Descendants(paper).OfType<LineEdit>().Single(edit => edit.Visible);
        editor.Text = "[bold]Name[/bold] [form]";
        editor.ForceSubmitText();
        var escapedName = FormattedMessage.EscapeText("[bold]Name[/bold] [form]");
        Assert.That(saved, Is.EqualTo("Name: " + escapedName + "\nPosition: [form]\nContents: [form]"), "Field values are plain text, not executable document markup.");
        paper.Populate(new PaperComponent.PaperBoundUserInterfaceState(saved!, []));
        paper.BeginFormEntry(0);
        editor = Descendants(paper).OfType<LineEdit>().Single(edit => edit.Visible);
        editor.Text = "Captain";
        editor.ForceSubmitText();
        Assert.That(saved, Is.EqualTo("Name: " + escapedName + "\nPosition: Captain\nContents: [form]"), "Literal escaped tags must not change field indices.");
    }

    private static void Click(BaseButton button)
    {
        button.MuteSounds = true;
        var pointer = new ScreenCoordinates(button.GlobalPixelPosition + button.PixelSize / 2, button.Window?.Id ?? default);
        foreach (var (name, state) in new[] { ("KeyBindDown", BoundKeyState.Down), ("KeyBindUp", BoundKeyState.Up) })
        {
            var args = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, pointer, default, button.Size / 2, button.PixelSize / 2);
            typeof(BaseButton).GetMethod(name, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, [args]);
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

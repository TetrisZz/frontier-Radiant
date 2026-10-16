using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using Content.Shared.Preferences;
using Content.Shared._radiant.GhostSkins;
using NUnit.Framework;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.PixelFormats;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class GhostSkinTest
{
    [TestCase(false, false, true)]
    [TestCase(false, true, true)]
    [TestCase(true, false, false)]
    [TestCase(true, true, true)]
    public void SupporterOnlySkinsRequireVerifiedAccess(bool supporterOnly, bool access, bool expected)
    {
        var skin = new GhostSkinPrototype { SupporterOnly = supporterOnly };
        Assert.That(GhostSkinRules.CanSelect(skin, access), Is.EqualTo(expected));
    }

    [Test]
    public void PreferenceIsAccountWideAndHasAFreeDefault()
    {
        var prefs = new PlayerPreferences(Array.Empty<KeyValuePair<int, ICharacterProfile>>(), 0, Robust.Shared.Maths.Color.White, []);
        Assert.That(prefs.GhostSkin, Is.EqualTo(GhostSkinRules.Default));
        prefs.GhostSkin = "GhostCat";
        var copied = new PlayerPreferences(prefs.Characters, 1, prefs.AdminOOCColor, prefs.ConstructionFavorites, prefs.GhostSkin);
        Assert.That(copied.GhostSkin, Is.EqualTo("GhostCat"));
    }

    [Test]
    public void FiveFreeAndFourSupporterSkinsHaveDirectionalSpritesAtOneTileScale()
    {
        var resources = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
        var yaml = new YamlStream();
        using (var reader = File.OpenText(Path.Combine(resources, "Prototypes/_radiant/GhostSkins/skins.yml")))
            yaml.Load(reader);
        var prototypes = ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>().ToArray();
        var skins = prototypes.Where(node => Text(node, "type") == "ghostSkin").ToArray();
        Assert.That(skins, Has.Length.EqualTo(9));
        var free = skins.Where(node => Text(node, "supporterOnly") != "true").ToArray();
        Assert.That(free.Select(node => Text(node, "id")), Is.EquivalentTo(new[]
            { "GhostOriginal", "GhostClassic", "GhostWhisper", "GhostWisp", "GhostCat" }));
        foreach (var skin in skins)
        {
            var donor = Text(skin, "supporterOnly") == "true";
            var spec = (YamlMappingNode) skin.Children[new YamlScalarNode("sprite")];
            var folder = Path.Combine(resources, "Textures", Text(spec, "sprite")!);
            using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "meta.json")));
            var root = metadata.RootElement;
            var size = root.GetProperty("size");
            var pixels = donor ? 96 : 32;
            Assert.That(size.GetProperty("x").GetInt32(), Is.EqualTo(pixels));
            Assert.That(size.GetProperty("y").GetInt32(), Is.EqualTo(pixels));
            var state = root.GetProperty("states").EnumerateArray().Single(s => s.GetProperty("name").GetString() == Text(spec, "state"));
            Assert.That(state.GetProperty("directions").GetInt32(), Is.EqualTo(4));
            using var atlas = Image.Load<Rgba32>(Path.Combine(folder, Text(spec, "state") + ".png"));
            Assert.That(atlas.Width, Is.EqualTo(pixels * 2));
            Assert.That(atlas.Height, Is.EqualTo(pixels * 2));
            if (donor)
            {
                var scales = Text(skin, "scale")!.Split(',').Select(value => float.Parse(value, CultureInfo.InvariantCulture));
                foreach (var scale in scales)
                    Assert.That(pixels * scale, Is.EqualTo(32).Within(0.001));
                Assert.That(atlas[0, 0].A, Is.Zero);
                for (var y = 0; y < atlas.Height; y++) for (var x = 0; x < atlas.Width; x++)
                    Assert.That(atlas[x, y].A, Is.LessThan(255));
            }
        }
        var action = prototypes.Single(node => Text(node, "id") == "ActionGhostSkin");
        var components = ((YamlSequenceNode) action.Children[new YamlScalarNode("components")]).Children.Cast<YamlMappingNode>();
        var actionComponent = components.Single(node => Text(node, "type") == "Action");
        Assert.That(Text(actionComponent, "checkCanInteract"), Is.EqualTo("false"));
        Assert.That(Text(actionComponent, "clientExclusive"), Is.EqualTo("true"));
    }

    private static string? Text(YamlMappingNode node, string key)
        => node.Children.TryGetValue(new YamlScalarNode(key), out var value) ? ((YamlScalarNode) value).Value : null;
}

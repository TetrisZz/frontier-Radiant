using System.IO;
using System.Linq;
using System.Text.Json;
using Content.Shared.Rounding;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class TowelDispenserTest
{
    private static string Resources => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));

    private static YamlMappingNode Find(string path, string id)
    {
        var yaml = new YamlStream();
        using var reader = File.OpenText(Path.Combine(Resources, "Prototypes", path));
        yaml.Load(reader);
        return ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>()
            .Single(node => node.Children[new YamlScalarNode("id")].ToString() == id);
    }

    private static YamlMappingNode Component(YamlMappingNode entity, string type)
        => ((YamlSequenceNode) entity.Children[new YamlScalarNode("components")]).Children.Cast<YamlMappingNode>()
            .Single(node => node.Children[new YamlScalarNode("type")].ToString() == type);

    [Test]
    public void OnlyFilledVariantSpawnsTenTowelsAndConstructionBuildsEmpty()
    {
        const string path = "_radiant/Entities/Structures/Furniture/towel_dispenser.yml";
        var bin = Component(Find(path, "TowelDispenser"), "Bin");
        Assert.That(bin.Children[new YamlScalarNode("maxItems")].ToString(), Is.EqualTo("10"));
        Assert.That(bin.Children[new YamlScalarNode("insertOnInteract")].ToString(), Is.EqualTo("true"));
        Assert.That(bin.Children.ContainsKey(new YamlScalarNode("initialContents")), Is.False);
        var whitelist = (YamlMappingNode) bin.Children[new YamlScalarNode("whitelist")];
        Assert.That(((YamlSequenceNode) whitelist.Children[new YamlScalarNode("tags")]).Children.Select(n => n.ToString()),
            Is.EqualTo(new[] { "Towel" }));
        var persist = Component(Find(path, "TowelDispenser"), "HLPersistOnShipSave");
        Assert.That(persist.Children[new YamlScalarNode("saveContents")].ToString(), Is.EqualTo("true"));
        var filled = Component(Find(path, "TowelDispenserFilled"), "ContainerFill");
        var containers = (YamlMappingNode) filled.Children[new YamlScalarNode("containers")];
        var contents = ((YamlSequenceNode) containers.Children[new YamlScalarNode("bin-container")]).Children;
        Assert.That(contents, Has.Count.EqualTo(10));
        Assert.That(contents.Select(n => n.ToString()), Is.All.EqualTo("TowelColorWhite"));
        var graph = Find("_radiant/Recipes/Construction/Graphs/towel_dispenser.yml", "TowelDispenser");
        var finished = ((YamlSequenceNode) graph.Children[new YamlScalarNode("graph")]).Children.Cast<YamlMappingNode>()
            .Single(n => n.Children[new YamlScalarNode("node")].ToString() == "finished");
        Assert.That(finished.Children[new YamlScalarNode("entity")].ToString(), Is.EqualTo("TowelDispenser"));
        var recipe = Find("_radiant/Recipes/Construction/towel_dispenser.yml", "TowelDispenser");
        Assert.That(recipe.Children[new YamlScalarNode("graph")].ToString(), Is.EqualTo("TowelDispenser"));
    }

    [Test]
    public void TowelTagPreservesExistingClothingTags()
    {
        var tags = Component(Find("Entities/Clothing/Multiple/towel.yml", "BaseTowel"), "Tag");
        Assert.That(((YamlSequenceNode) tags.Children[new YamlScalarNode("tags")]).Children.Select(n => n.ToString()),
            Is.EquivalentTo(new[] { "ClothMade", "Recyclable", "WhitelistChameleon", "Towel" }));
        Assert.That(Find("_radiant/tags.yml", "Towel"), Is.Not.Null);
    }

    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(5, 1)]
    [TestCase(6, 2)]
    [TestCase(10, 2)]
    public void FillThresholdsSelectExistingLicensedSprites(int count, int expected)
    {
        var visual = Component(Find("_radiant/Entities/Structures/Furniture/towel_dispenser.yml", "TowelDispenser"), "ContainerCountVisualizer");
        var max = int.Parse(visual.Children[new YamlScalarNode("maxCount")].ToString());
        var levels = int.Parse(visual.Children[new YamlScalarNode("maxFillLevels")].ToString());
        Assert.That(ContentHelpers.RoundToLevels(count, max, levels), Is.EqualTo(expected));
        var folder = Path.Combine(Resources, "Textures/_radiant/Structures/Furniture/towel_dispenser.rsi");
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "meta.json")));
        Assert.That(json.RootElement.GetProperty("license").GetString(), Is.EqualTo("CC-BY-SA-3.0"));
        Assert.That(json.RootElement.GetProperty("copyright").GetString(), Does.Contain("icons/obj/structures.dmi"));
        Assert.That(json.RootElement.GetProperty("states").EnumerateArray().Select(s => s.GetProperty("name").GetString()),
            Does.Contain("fill-" + expected));
        Assert.That(File.Exists(Path.Combine(folder, "fill-" + expected + ".png")), Is.True);
    }
}

using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

public sealed class OfficeChairTest
{
    private static string Resources => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));

    private static YamlNode Read(string path)
    {
        var yaml = new YamlStream();
        using var reader = File.OpenText(Path.Combine(Resources, path));
        yaml.Load(reader);
        return yaml.Documents[0].RootNode;
    }

    private static string Value(YamlMappingNode node, string key) => node.Children[new YamlScalarNode(key)].ToString();

    private static YamlMappingNode Find(string path, string id)
        => ((YamlSequenceNode) Read("Prototypes/" + path)).Children.Cast<YamlMappingNode>()
            .Single(node => Value(node, "id") == id);

    private static YamlMappingNode Component(YamlMappingNode node, string type)
        => ((YamlSequenceNode) node.Children[new YamlScalarNode("components")]).Children.Cast<YamlMappingNode>()
            .Single(component => Value(component, "type") == type);

    [TestCase("ChairOfficeWhiteRadiant", "ChairOfficeLight", "officechair_white")]
    [TestCase("ChairOfficeDarkRadiant", "ChairOfficeDark", "officechair_dark")]
    public void StandardChairsUseTheImportedSpritesAndLegacyIdsMigrate(string legacy, string current, string state)
    {
        var migrations = (YamlMappingNode) Read("nf_migration.yml");
        Assert.That(Value(migrations, legacy), Is.EqualTo(current));
        var removed = ((YamlSequenceNode) Read("Prototypes/_radiant/Entities/Structures/office_furniture.yml"))
            .Children.Cast<YamlMappingNode>();
        Assert.That(removed.Select(node => Value(node, "id")), Does.Not.Contain(legacy));
        var chair = Find("Entities/Structures/Furniture/chairs.yml", current);
        var sprite = Component(chair, "Sprite");
        const string asset = "_radiant/Structures/Furniture/office_chairs.rsi";
        Assert.That(Value(sprite, "sprite"), Is.EqualTo(asset));
        Assert.That(Value(sprite, "state"), Is.EqualTo(state));
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Resources, "Textures", asset, "meta.json")));
        var entry = json.RootElement.GetProperty("states").EnumerateArray().Single(node => node.GetProperty("name").GetString() == state);
        Assert.That(entry.GetProperty("directions").GetInt32(), Is.EqualTo(4));
        Assert.That(File.Exists(Path.Combine(Resources, "Textures", asset, state + ".png")), Is.True);
        Assert.That(Value(Component(chair, "Construction"), "graph"), Is.EqualTo("Seat"));
    }

    [Test]
    public void GreyOfficeChairBuildsAndDisassemblesForTwoSteelSheets()
    {
        var chair = Find("_radiant/Entities/Structures/Furniture/chairs.yml", "ChairOfficeGrey");
        var recipe = Find("_radiant/Recipes/Construction/furniture.yml", "ChairOfficeGrey");
        var construction = Component(chair, "Construction");
        Assert.That(Value(recipe, "graph"), Is.EqualTo(Value(construction, "graph")));
        Assert.That(Value(recipe, "targetNode"), Is.EqualTo(Value(construction, "node")));
        Assert.That(Value(recipe, "category"), Is.EqualTo("construction-category-furniture"));
        var graph = Find("_radiant/Recipes/Construction/Graphs/furniture/chairs.yml", Value(recipe, "graph"));
        var nodes = ((YamlSequenceNode) graph.Children[new YamlScalarNode("graph")]).Children.Cast<YamlMappingNode>().ToArray();
        var start = nodes.Single(node => Value(node, "node") == Value(recipe, "startNode"));
        var build = ((YamlSequenceNode) start.Children[new YamlScalarNode("edges")]).Children.Cast<YamlMappingNode>()
            .Single(edge => Value(edge, "to") == Value(recipe, "targetNode"));
        var material = (YamlMappingNode) ((YamlSequenceNode) build.Children[new YamlScalarNode("steps")]).Children.Single();
        Assert.That(Value(material, "material"), Is.EqualTo("Steel"));
        Assert.That(Value(material, "amount"), Is.EqualTo("2"));
        var target = nodes.Single(node => Value(node, "node") == Value(recipe, "targetNode"));
        Assert.That(Value(target, "entity"), Is.EqualTo("ChairOfficeGrey"));
        var dismantle = (YamlMappingNode) ((YamlSequenceNode) target.Children[new YamlScalarNode("edges")]).Children.Single();
        Assert.That(Value(dismantle, "to"), Is.EqualTo("start"));
        var refund = (YamlMappingNode) ((YamlSequenceNode) dismantle.Children[new YamlScalarNode("completed")]).Children.Single();
        Assert.That(Value(refund, "prototype"), Is.EqualTo("SheetSteel1"));
        Assert.That(Value(refund, "amount"), Is.EqualTo("2"));
        var tool = (YamlMappingNode) ((YamlSequenceNode) dismantle.Children[new YamlScalarNode("steps")]).Children.Single();
        Assert.That(Value(tool, "tool"), Is.EqualTo("Screwing"));
    }

    [TestCase("ru-RU", "серое офисное кресло")]
    [TestCase("en-US", "grey office chair")]
    public void GreyOfficeChairHasALocalizedNameAndDescription(string locale, string name)
    {
        var text = File.ReadAllText(Path.Combine(Resources, "Locale", locale, "_radiant/entities/additional_furniture.ftl"));
        Assert.That(text, Does.Contain("ent-ChairOfficeGrey = " + name));
        Assert.That(text, Does.Contain(".desc = { ent-ChairOfficeLight.desc }"));
    }
}

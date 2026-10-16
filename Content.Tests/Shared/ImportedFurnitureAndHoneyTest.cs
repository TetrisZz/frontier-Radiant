using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class ImportedFurnitureAndHoneyTest
{
    private static string Resources => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
        "..", "..", "Resources"));

    private static YamlNode Field(YamlMappingNode node, string name) => node.Children[new YamlScalarNode(name)];

    private static YamlMappingNode[] Read(string path)
    {
        var yaml = new YamlStream();
        using var reader = File.OpenText(Path.Combine(Resources, path));
        yaml.Load(reader);
        return ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>().ToArray();
    }

    [Test]
    public void DressersKeepOriginalNamesAndContentsThroughInheritance()
    {
        var entities = Read("Prototypes/_radiant/Entities/Structures/Furniture/dressers.yml");
        Assert.That(entities.Length, Is.EqualTo(11));
        foreach (var entity in entities)
        {
            var id = ((YamlScalarNode) Field(entity, "id")).Value!;
            Assert.That(((YamlScalarNode) Field(entity, "parent")).Value, Is.EqualTo(id["Radiant".Length..]));
            Assert.That(entity.Children.ContainsKey(new YamlScalarNode("name")), Is.False);
            Assert.That(entity.Children.ContainsKey(new YamlScalarNode("suffix")), Is.False);
            var components = ((YamlSequenceNode) Field(entity, "components")).Children.Cast<YamlMappingNode>().ToArray();
            Assert.That(components.Length, Is.EqualTo(1));
            Assert.That(((YamlScalarNode) Field(components[0], "type")).Value, Is.EqualTo("Sprite"));
        }
    }

    [TestCase("wooden_television", "off", 4, 1)]
    [TestCase("wooden_television", "on", 4, 30)]
    [TestCase("wooden_television", "broken", 4, 3)]
    [TestCase("honey_vendor", "normal", 1, 4)]
    [TestCase("honey_vendor", "off", 1, 1)]
    [TestCase("honey_vendor", "broken", 1, 1)]
    [TestCase("honey_vendor", "panel", 1, 1)]
    public void OriginalDirectionsAndAnimationsArePresent(string folder, string name, int directions, int frames)
    {
        var directory = Path.Combine(Resources, $"Textures/_radiant/Structures/Machines/{folder}.rsi");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "meta.json")));
        var root = metadata.RootElement;
        Assert.That(root.GetProperty("size").GetProperty("x").GetInt32(), Is.EqualTo(32));
        Assert.That(root.GetProperty("size").GetProperty("y").GetInt32(), Is.EqualTo(32));
        var state = root.GetProperty("states").EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
        Assert.That(state.TryGetProperty("directions", out var dirs) ? dirs.GetInt32() : 1, Is.EqualTo(directions));
        if (frames > 1)
        {
            Assert.That(state.GetProperty("delays").GetArrayLength(), Is.EqualTo(directions));
            foreach (var delays in state.GetProperty("delays").EnumerateArray())
                Assert.That(delays.GetArrayLength(), Is.EqualTo(frames));
        }
        var png = File.ReadAllBytes(Path.Combine(directory, name + ".png"));
        Assert.That(Dimension(png, 16), Is.EqualTo(32 * directions * frames));
        Assert.That(Dimension(png, 20), Is.EqualTo(32));
    }

    private static int Dimension(byte[] bytes, int start) =>
        bytes[start] << 24 | bytes[start + 1] << 16 | bytes[start + 2] << 8 | bytes[start + 3];

    [Test]
    public void HoneyVendorIncludesFullHiddenAndPremiumStock()
    {
        var inventory = Read("Prototypes/_radiant/Entities/Structures/Machines/honey_vendor.yml")
            .Single(n => ((YamlScalarNode) Field(n, "id")).Value == "HoneyVendorInventory");
        var ordinary = (YamlMappingNode) Field(inventory, "startingInventory");
        Assert.That(ordinary.Children.Count, Is.EqualTo(11));
        Assert.That(((YamlScalarNode) Field(ordinary, "DrinkBuzzFuzzCan")).Value, Is.EqualTo("5"));
        var hidden = (YamlMappingNode) Field(inventory, "contrabandInventory");
        Assert.That(((YamlScalarNode) Field(hidden, "DrinkThirteenLokoCan")).Value, Is.EqualTo("6"));
        var premium = (YamlMappingNode) Field(inventory, "emaggedInventory");
        Assert.That(((YamlScalarNode) Field(premium, "DrinkCarpLiteBottle")).Value, Is.EqualTo("1"));
        Assert.That(((YamlScalarNode) Field(premium, "DrinkGreyBullCan")).Value, Is.EqualTo("1"));
    }

    [Test]
    public void TelevisionConstructionKeepsItsOwnFrameAndBrokenSprite()
    {
        var graph = Read("Prototypes/_radiant/Recipes/Construction/Graphs/wooden_television.yml").Single();
        var nodes = ((YamlSequenceNode) Field(graph, "graph")).Children.Cast<YamlMappingNode>().ToArray();
        foreach (var node in nodes.Where(n => n.Children.TryGetValue(new YamlScalarNode("entity"), out var entity) && entity is YamlScalarNode))
        {
            var id = ((YamlScalarNode) Field(node, "entity")).Value;
            Assert.That(id, Is.AnyOf("ComputerTelevisionRadiantFrame", "ComputerTelevisionRadiantBroken"));
        }
    }
}

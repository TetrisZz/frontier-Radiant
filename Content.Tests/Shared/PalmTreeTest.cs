using System.IO;
using System.Linq;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

public sealed class PalmTreeTest
{
    [Test]
    public void PalmsInheritTheTenMinuteCoconutSpawner()
    {
        var resources = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
        using var reader = File.OpenText(Path.Combine(resources, "Prototypes/_NF/Entities/Objects/Decoration/flora.yml"));
        var yaml = new YamlStream();
        yaml.Load(reader);
        var prototypes = ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>().ToArray();
        string Value(YamlMappingNode node, string key) => node.Children[new YamlScalarNode(key)].ToString();
        var palm = prototypes.Single(node => Value(node, "id") == "FloraTreePalm");
        Assert.That(Value(palm, "parent"), Is.EqualTo("BaseTreePalm"));
        var parent = prototypes.Single(node => Value(node, "id") == "BaseTreePalm");
        var components = ((YamlSequenceNode) parent.Children[new YamlScalarNode("components")]).Children.Cast<YamlMappingNode>();
        var spawner = components.Single(node => Value(node, "type") == "TimedSpawner");
        Assert.That(Value(spawner, "intervalSeconds"), Is.EqualTo("600"));
        Assert.That(((YamlSequenceNode) spawner.Children[new YamlScalarNode("prototypes")]).Children.Select(node => node.ToString()),
            Is.EqualTo(new[] { "DrinkCoconut" }));
    }
}

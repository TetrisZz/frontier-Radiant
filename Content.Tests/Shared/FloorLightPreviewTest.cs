using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Globalization;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class FloorLightPreviewTest
{
    private readonly Dictionary<string, YamlMappingNode> _entities = new();

    [OneTimeSetUp]
    public void ReadLightingPrototypes()
    {
        var resources = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
        var files = Directory.GetFiles(Path.Combine(resources,
            "Prototypes/_radiant/Entities/Structures/Lighting"), "*.yml").Concat(new[]
        {
            Path.Combine(resources, "Prototypes/Entities/Structures/Lighting/base_lighting.yml"),
            Path.Combine(resources, "Prototypes/Entities/Structures/Lighting/ground_lighting.yml"),
            Path.Combine(resources, "Prototypes/_NF/Entities/Structures/Lighting/base_lighting.yml"),
        });
        foreach (var file in files)
        {
            using var reader = File.OpenText(file);
            var yaml = new YamlStream();
            yaml.Load(reader);
            foreach (var node in ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>())
                _entities.Add(((YamlScalarNode) Field(node, "id")).Value!, node);
        }
    }

    private string? InheritedField(string id, string component, string field)
    {
        if (!_entities.TryGetValue(id, out var entity))
            return null;
        if (entity.Children.TryGetValue(new YamlScalarNode("components"), out var components))
        {
            var match = ((YamlSequenceNode) components).Children.Cast<YamlMappingNode>()
                .SingleOrDefault(node => ((YamlScalarNode) Field(node, "type")).Value == component);
            if (match != null && match.Children.TryGetValue(new YamlScalarNode(field), out var value))
                return ((YamlScalarNode) value).Value;
        }
        if (!entity.Children.TryGetValue(new YamlScalarNode("parent"), out var parent))
            return null;
        var parents = parent is YamlSequenceNode sequence ? sequence.Children : new[] { parent };
        foreach (var entry in parents)
        {
            var value = InheritedField(((YamlScalarNode) entry).Value!, component, field);
            if (value != null)
                return value;
        }
        return null;
    }

    [Test]
    public void AllAddedFixturesWithBulbsHaveMatchingPreviewLighting()
    {
        var count = 0;
        foreach (var (id, entity) in _entities)
        {
            if ((!id.StartsWith("Radiant") && id != "NFPoweredlightShielded" && id != "NFPoweredSmallLightShielded")
                || entity.Children.ContainsKey(new YamlScalarNode("abstract")))
                continue;
            var bulbId = InheritedField(id, "PoweredLight", "hasLampOnSpawn");
            if (bulbId is null or "null")
                continue;
            var bulb = Component("Entities/Objects/Power/lights.yml", bulbId, "LightBulb");
            Assert.That(InheritedField(id, "PointLight", "enabled"), Is.EqualTo("true"), id);
            foreach (var (preview, installed) in new[]
                     { ("radius", "lightRadius"), ("energy", "lightEnergy"), ("softness", "lightSoftness") })
                Assert.That(double.Parse(InheritedField(id, "PointLight", preview)!, CultureInfo.InvariantCulture),
                    Is.EqualTo(double.Parse(((YamlScalarNode) Field(bulb, installed)).Value!, CultureInfo.InvariantCulture)), id);
            Assert.That(InheritedField(id, "PointLight", "color"),
                Is.EqualTo(((YamlScalarNode) Field(bulb, "color")).Value), id);
            count++;
        }
        Assert.That(count, Is.EqualTo(25));
    }

    [Test]
    public void AllEmptyFixturesRemainDarkAndCraftsStillProduceEmptyFixtures()
    {
        var empty = _entities.Keys.Where(id => id.StartsWith("Radiant") && id.EndsWith("Empty")).ToArray();
        Assert.That(empty.Length, Is.EqualTo(16));
        foreach (var id in empty.Concat(new[] { "NFPoweredlightShieldedEmpty", "NFPoweredSmallLightShieldedEmpty" }))
        {
            Assert.That(InheritedField(id, "PoweredLight", "hasLampOnSpawn"), Is.AnyOf(null, "null"), id);
            Assert.That(InheritedField(id, "PointLight", "enabled"), Is.EqualTo("false"), id);
        }
        var resources = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
        using var reader = File.OpenText(Path.Combine(resources,
            "Prototypes/_radiant/Recipes/Construction/Graphs/decorative_lights.yml"));
        var yaml = new YamlStream();
        yaml.Load(reader);
        var graph = (YamlMappingNode) ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Single();
        var nodes = ((YamlSequenceNode) Field(graph, "graph")).Children.Cast<YamlMappingNode>();
        foreach (var node in nodes)
            if (node.Children.TryGetValue(new YamlScalarNode("entity"), out var prototype))
                Assert.That(((YamlScalarNode) prototype).Value, Does.EndWith("Empty"));
    }

    private static YamlNode Field(YamlMappingNode node, string name) => node.Children[new YamlScalarNode(name)];

    private static YamlMappingNode Component(string file, string id, string type)
    {
        var resources = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));
        using var reader = File.OpenText(Path.Combine(resources, "Prototypes", file));
        var yaml = new YamlStream();
        yaml.Load(reader);
        var entity = ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>()
            .Single(node => ((YamlScalarNode) Field(node, "id")).Value == id);
        return ((YamlSequenceNode) Field(entity, "components")).Children.Cast<YamlMappingNode>()
            .Single(node => ((YamlScalarNode) Field(node, "type")).Value == type);
    }

    [Test]
    public void ShieldedFloorLightPreviewMatchesTheInstalledBulb()
    {
        var light = Component("_radiant/Entities/Structures/Lighting/decorative_lights_shielded.yml",
            "RadiantFloorLightShielded", "PointLight");
        var bulb = Component("Entities/Objects/Power/lights.yml", "LightBulb", "LightBulb");
        Assert.That(((YamlScalarNode) Field(light, "enabled")).Value, Is.EqualTo("true"));
        foreach (var (preview, installed) in new[]
                 { ("radius", "lightRadius"), ("softness", "lightSoftness"), ("color", "color") })
            Assert.That(((YamlScalarNode) Field(light, preview)).Value,
                Is.EqualTo(((YamlScalarNode) Field(bulb, installed)).Value));
        Assert.That(double.Parse(((YamlScalarNode) Field(light, "energy")).Value!, System.Globalization.CultureInfo.InvariantCulture),
            Is.EqualTo(double.Parse(((YamlScalarNode) Field(bulb, "lightEnergy")).Value!, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Test]
    public void EmptyShieldedFloorLightDoesNotSpawnABulbOrPreviewLight()
    {
        const string file = "_radiant/Entities/Structures/Lighting/decorative_lights_empty.yml";
        var light = Component(file, "RadiantFloorLightShieldedEmpty", "PointLight");
        var powered = Component(file, "RadiantFloorLightShieldedEmpty", "PoweredLight");
        Assert.That(((YamlScalarNode) Field(light, "enabled")).Value, Is.EqualTo("false"));
        Assert.That(((YamlScalarNode) Field(powered, "hasLampOnSpawn")).Value, Is.EqualTo("null"));
    }
}

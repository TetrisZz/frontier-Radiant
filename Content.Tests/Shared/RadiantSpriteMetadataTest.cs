using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class RadiantSpriteMetadataTest
{
    private static string Resources => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
        "..", "..", "Resources"));

    [TestCase("_EE/Contractors/nt_identity_card.rsi")]
    [TestCase("_radiant/Objects/Documents/ServiceCredentials.rsi")]
    public void GeneratedSpritesCreditHomka(string folder)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(Resources, "Textures", folder, "meta.json")));
        Assert.That(metadata.RootElement.GetProperty("copyright").GetString(),
            Does.StartWith("Made by homka for Radiant Sector"));
    }

    [TestCase("dvb_closed")]
    [TestCase("fleet_closed")]
    public void CredentialCoversKeepTheir32PixelRgbaCanvas(string state)
    {
        var png = File.ReadAllBytes(Path.Combine(Resources,
            "Textures/_radiant/Objects/Documents/ServiceCredentials.rsi", state + ".png"));
        Assert.That(png[19], Is.EqualTo(32));
        Assert.That(png[23], Is.EqualTo(32));
        Assert.That(png[25], Is.EqualTo(6));
    }

    [TestCase("departmental_crates.rsi", "crates.dmi")]
    [TestCase("departmental_tall_crates.rsi", "crates_new.dmi")]
    [TestCase("departmental_lockers.rsi", "closet.dmi")]
    public void ImportedStorageSpritesCreditTheirSource(string folder, string source)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(Resources, "Textures",
            "_radiant/Structures/Storage", folder, "meta.json")));
        var credit = metadata.RootElement.GetProperty("copyright").GetString();
        Assert.That(credit, Does.Contain("taken from BlueMoon Station"));
        Assert.That(credit, Does.Contain("icons/obj/" + source));
        Assert.That(metadata.RootElement.GetProperty("license").GetString(), Is.EqualTo("CC-BY-SA-3.0"));
    }

    [Test]
    public void ExtinguisherMountIncludesAllBlueMoonStates()
    {
        var folder = Path.Combine(Resources, "Textures/_radiant/Structures/Wallmounts/extinguisher_cabinet.rsi");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "meta.json")));
        Assert.That(metadata.RootElement.GetProperty("copyright").GetString(), Does.Contain("BlueMoon Station, icons/obj/wallmounts.dmi"));
        Assert.That(metadata.RootElement.GetProperty("license").GetString(), Is.EqualTo("CC-BY-SA-3.0"));
        var states = metadata.RootElement.GetProperty("states").EnumerateArray()
            .Select(state => state.GetProperty("name").GetString()).ToArray();
        Assert.That(states, Is.EquivalentTo(new[]
        {
            "extinguisher_empty_closed", "extinguisher_empty_open", "extinguisher_mini_open",
            "extinguisher_standard_open", "extinguisher_advanced_open", "extinguisher_mini_closed",
            "extinguisher_closed", "extinguisher_advanced_closed"
        }));
        foreach (var state in states)
        {
            var png = File.ReadAllBytes(Path.Combine(folder, state + ".png"));
            Assert.That(png[19], Is.EqualTo(32));
            Assert.That(png[23], Is.EqualTo(32));
            Assert.That(png[25], Is.EqualTo(6));
        }
    }

    [Test]
    public void ElectricalCrateHasOnlyOneSwitchableBaseLayer()
    {
        var yaml = new YamlStream();
        using var reader = File.OpenText(Path.Combine(Resources,
            "Prototypes/_radiant/Entities/Structures/departmental_storage.yml"));
        yaml.Load(reader);
        var entity = ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.OfType<YamlMappingNode>()
            .Single(node => node.Children.TryGetValue(new YamlScalarNode("id"), out var id) &&
                            ((YamlScalarNode) id).Value == "RadiantDepartmentalTallCrateElectrical");
        var components = ((YamlSequenceNode) entity.Children[new YamlScalarNode("components")])
            .Children.Cast<YamlMappingNode>().ToArray();
        var sprite = components.Single(node => ((YamlScalarNode) node.Children[new YamlScalarNode("type")]).Value == "Sprite");
        var baseLayer = ((YamlSequenceNode) sprite.Children[new YamlScalarNode("layers")]).Children
            .Cast<YamlMappingNode>().Single(node => node.Children.TryGetValue(new YamlScalarNode("state"), out var state) &&
                                                   ((YamlScalarNode) state).Value == "engi_e_crate");
        Assert.That(((YamlSequenceNode) baseLayer.Children[new YamlScalarNode("map")]).Children
            .Cast<YamlScalarNode>().Select(node => node.Value), Does.Contain("enum.StorageVisualLayers.Base"));
        var visuals = components.Single(node => ((YamlScalarNode) node.Children[new YamlScalarNode("type")]).Value == "EntityStorageVisuals");
        Assert.That(((YamlScalarNode) visuals.Children[new YamlScalarNode("stateBaseOpen")]).Value,
            Is.EqualTo("engi_e_crateopen"));
        var rsi = Path.Combine(Resources, "Textures/_radiant/Structures/Storage/departmental_tall_crates.rsi");
        Assert.That(File.ReadAllBytes(Path.Combine(rsi, "engi_e_crateopen.png")),
            Is.Not.EqualTo(File.ReadAllBytes(Path.Combine(rsi, "engi_e_crate.png"))));
    }
}

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

    [TestCase("_radiant/Structures/Lighting/decorative_lamp_post.rsi", 32, 64, 2)]
    [TestCase("_radiant/Structures/Lighting/ornate_lamp_post.rsi", 96, 160, 15)]
    [TestCase("_radiant/Objects/Fun/Plushies/companions.rsi", 32, 32, 2)]
    public void ImportedLampsAndPlushiesKeepOriginalPixelDimensions(string folder, int width, int height, int stateCount)
    {
        var directory = Path.Combine(Resources, "Textures", folder);
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "meta.json")));
        var root = metadata.RootElement;
        Assert.That(root.GetProperty("size").GetProperty("x").GetInt32(), Is.EqualTo(width));
        Assert.That(root.GetProperty("size").GetProperty("y").GetInt32(), Is.EqualTo(height));
        Assert.That(root.GetProperty("license").GetString(), Is.EqualTo("CC-BY-SA-3.0"));
        Assert.That(root.GetProperty("states").GetArrayLength(), Is.EqualTo(stateCount));
        foreach (var state in root.GetProperty("states").EnumerateArray())
        {
            var directions = state.TryGetProperty("directions", out var count) ? count.GetInt32() : 1;
            var bytes = File.ReadAllBytes(Path.Combine(directory, state.GetProperty("name").GetString() + ".png"));
            Assert.That(PngDimension(bytes, 16), Is.EqualTo(width * directions));
            Assert.That(PngDimension(bytes, 20), Is.EqualTo(height));
            Assert.That(bytes[25], Is.EqualTo(6));
        }
    }

    private static int PngDimension(byte[] bytes, int start) =>
        bytes[start] << 24 | bytes[start + 1] << 16 | bytes[start + 2] << 8 | bytes[start + 3];

    [TestCase("normal")]
    [TestCase("fill1")]
    [TestCase("fill2")]
    [TestCase("fill3")]
    public void SmartFridgeFillStatesRetainAllAnimationFrames(string name)
    {
        var directory = Path.Combine(Resources, "Textures/_radiant/Structures/Machines/smart_fridge.rsi");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "meta.json")));
        var root = metadata.RootElement;
        Assert.That(root.GetProperty("size").GetProperty("x").GetInt32(), Is.EqualTo(32));
        Assert.That(root.GetProperty("size").GetProperty("y").GetInt32(), Is.EqualTo(32));
        var state = root.GetProperty("states").EnumerateArray().Single(s => s.GetProperty("name").GetString() == name);
        var delays = state.GetProperty("delays")[0].EnumerateArray().Select(d => d.GetDouble()).ToArray();
        Assert.That(delays, Is.EqualTo(Enumerable.Repeat(0.2, 7)));
        var png = File.ReadAllBytes(Path.Combine(directory, name + ".png"));
        Assert.That(PngDimension(png, 16), Is.EqualTo(32 * 7));
        Assert.That(PngDimension(png, 20), Is.EqualTo(32));
    }

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

    [TestCase("Crates/classic_medical.rsi", "crates.dmi")]
    [TestCase("Crates/tall_medical.rsi", "crates_new.dmi")]
    [TestCase("departmental_lockers.rsi", "closet.dmi")]
    public void ImportedStorageSpritesCreditTheirSource(string folder, string source)
    {
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(Resources, "Textures",
            "_radiant/Structures/Storage", folder, "meta.json")));
        var credit = metadata.RootElement.GetProperty("copyright").GetString();
        Assert.That(credit, Does.Contain("BlueMoon Station"));
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
    public void ColorMateKeepsAnimationsUncoveredByStaticLayers()
    {
        var yaml = new YamlStream();
        using var reader = File.OpenText(Path.Combine(Resources,
            "Prototypes/_radiant/Entities/Structures/Machines/paint_vendor.yml"));
        yaml.Load(reader);
        var entity = ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.OfType<YamlMappingNode>()
            .Single(node => ((YamlScalarNode) node.Children[new YamlScalarNode("id")]).Value == "VendingMachinePaint");
        var components = ((YamlSequenceNode) entity.Children[new YamlScalarNode("components")])
            .Children.Cast<YamlMappingNode>().ToArray();
        var sprite = components.Single(node => ((YamlScalarNode) node.Children[new YamlScalarNode("type")]).Value == "Sprite");
        var layers = ((YamlSequenceNode) sprite.Children[new YamlScalarNode("layers")]).Children
            .Cast<YamlMappingNode>().ToArray();
        Assert.That(layers.Any(layer => ((YamlScalarNode) layer.Children[new YamlScalarNode("state")]).Value == "light"), Is.False);
        var panel = layers.Single(layer => ((YamlScalarNode) layer.Children[new YamlScalarNode("state")]).Value == "panel");
        Assert.That(((YamlScalarNode) panel.Children[new YamlScalarNode("visible")]).Value, Is.EqualTo("false"));
        var machine = components.Single(node => ((YamlScalarNode) node.Children[new YamlScalarNode("type")]).Value == "VendingMachine");
        Assert.That(((YamlScalarNode) machine.Children[new YamlScalarNode("screenState")]).Value, Is.EqualTo("null"));

        var directory = Path.Combine(Resources, "Textures/_radiant/Structures/Machines/paint_vendor.rsi");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(directory, "meta.json")));
        foreach (var (name, expected) in new[] { ("normal", 14), ("vend", 8) })
        {
            var state = metadata.RootElement.GetProperty("states").EnumerateArray()
                .Single(s => s.GetProperty("name").GetString() == name);
            Assert.That(state.GetProperty("delays")[0].GetArrayLength(), Is.EqualTo(expected));
            var png = File.ReadAllBytes(Path.Combine(directory, name + ".png"));
            Assert.That(PngDimension(png, 16) * PngDimension(png, 20) / (32 * 32), Is.GreaterThanOrEqualTo(expected));
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
                                                   ((YamlScalarNode) state).Value == "closed");
        Assert.That(((YamlSequenceNode) baseLayer.Children[new YamlScalarNode("map")]).Children
            .Cast<YamlScalarNode>().Select(node => node.Value), Does.Contain("enum.StorageVisualLayers.Base"));
        var visuals = components.Single(node => ((YamlScalarNode) node.Children[new YamlScalarNode("type")]).Value == "EntityStorageVisuals");
        Assert.That(((YamlScalarNode) visuals.Children[new YamlScalarNode("stateBaseOpen")]).Value,
            Is.EqualTo("open"));
        var rsi = Path.Combine(Resources, "Textures/_radiant/Structures/Storage/Crates/tall_electrical.rsi");
        Assert.That(File.ReadAllBytes(Path.Combine(rsi, "open.png")),
            Is.Not.EqualTo(File.ReadAllBytes(Path.Combine(rsi, "closed.png"))));
    }

    [TestCase("classic_medical", 32, false)]
    [TestCase("classic_medical_secure", 32, true)]
    [TestCase("classic_science", 32, false)]
    [TestCase("classic_science_secure", 32, true)]
    [TestCase("classic_security_gear", 32, true)]
    [TestCase("classic_engineering", 32, false)]
    [TestCase("classic_engineering_secure", 32, true)]
    [TestCase("tall_electrical", 48, false)]
    [TestCase("tall_engineering", 48, false)]
    [TestCase("tall_freezer", 48, false)]
    [TestCase("tall_medical", 48, false)]
    [TestCase("tall_radiation", 48, false)]
    [TestCase("tall_security_gear", 48, true)]
    public void CrateVariantsKeepTheirOwnDimensionsAndLockIndicators(string name, int height, bool secure)
    {
        var folder = Path.Combine(Resources, "Textures/_radiant/Structures/Storage/Crates", name + ".rsi");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "meta.json")));
        Assert.That(metadata.RootElement.GetProperty("size").GetProperty("x").GetInt32(), Is.EqualTo(32));
        Assert.That(metadata.RootElement.GetProperty("size").GetProperty("y").GetInt32(), Is.EqualTo(height));
        var states = metadata.RootElement.GetProperty("states").EnumerateArray()
            .Select(state => state.GetProperty("name").GetString()).ToArray();
        Assert.That(states, Is.EquivalentTo(secure
            ? new[] { "closed", "open", "locked", "unlocked", "transparent" }
            : new[] { "closed", "open", "transparent" }));
        foreach (var state in states)
        {
            var png = File.ReadAllBytes(Path.Combine(folder, state + ".png"));
            Assert.That(PngDimension(png, 16), Is.EqualTo(32));
            Assert.That(PngDimension(png, 20), Is.EqualTo(height));
        }
        if (secure)
            Assert.That(File.ReadAllBytes(Path.Combine(folder, "locked.png")),
                Is.Not.EqualTo(File.ReadAllBytes(Path.Combine(folder, "unlocked.png"))));
    }

    [TestCase("RadiantDepartmentalClassicSecureCrateBase")]
    [TestCase("RadiantDepartmentalTallSecureCrateBase")]
    public void SecureCratesUseTheirOwnUnshiftedLockLayer(string id)
    {
        using var reader = File.OpenText(Path.Combine(Resources,
            "Prototypes/_radiant/Entities/Structures/departmental_storage.yml"));
        var yaml = new YamlStream();
        yaml.Load(reader);
        var entity = ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>()
            .Single(node => ((YamlScalarNode) node.Children[new YamlScalarNode("id")]).Value == id);
        var components = ((YamlSequenceNode) entity.Children[new YamlScalarNode("components")])
            .Children.Cast<YamlMappingNode>().ToArray();
        var sprite = components.Single(node => ((YamlScalarNode) node.Children[new YamlScalarNode("type")]).Value == "Sprite");
        var layer = ((YamlSequenceNode) sprite.Children[new YamlScalarNode("layers")]).Children.Cast<YamlMappingNode>()
            .Single(node => node.Children.TryGetValue(new YamlScalarNode("state"), out var state)
                && ((YamlScalarNode) state).Value == "locked");
        Assert.That(layer.Children.ContainsKey(new YamlScalarNode("sprite")), Is.False);
        Assert.That(layer.Children.ContainsKey(new YamlScalarNode("offset")), Is.False);
        var visuals = components.Single(node => ((YamlScalarNode) node.Children[new YamlScalarNode("type")]).Value == "LockVisuals");
        Assert.That(((YamlScalarNode) visuals.Children[new YamlScalarNode("stateLocked")]).Value, Is.EqualTo("locked"));
        Assert.That(((YamlScalarNode) visuals.Children[new YamlScalarNode("stateUnlocked")]).Value, Is.EqualTo("unlocked"));
    }
}

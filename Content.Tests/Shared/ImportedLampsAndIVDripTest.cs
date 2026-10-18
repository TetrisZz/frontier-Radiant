using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class ImportedLampsAndIVDripTest
{
    private static string Resources => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory,
        "..", "..", "Resources"));

    [TestCase("Objects/Misc/Lights/lamp.rsi", "lamp")]
    [TestCase("Objects/Misc/Lights/lamp.rsi", "lamp-on")]
    [TestCase("Objects/Misc/Lights/lampgreen.rsi", "lampgreen")]
    [TestCase("Objects/Misc/Lights/lampgreen.rsi", "lampgreen-on")]
    [TestCase("Objects/Misc/Lights/lights.rsi", "bananalamp")]
    [TestCase("Objects/Misc/Lights/lights.rsi", "bananalamp_on")]
    [TestCase("Objects/Misc/Lights/lampint.rsi", "lamp-int")]
    [TestCase("Objects/Misc/Lights/lampint.rsi", "lamp-int-on")]
    public void LampsKeepOriginal32PixelSingleDirectionStates(string folder, string name)
    {
        var path = Path.Combine(Resources, "Textures", folder);
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "meta.json")));
        var state = metadata.RootElement.GetProperty("states").EnumerateArray()
            .Single(s => s.GetProperty("name").GetString() == name);
        Assert.That(state.TryGetProperty("directions", out var dirs) ? dirs.GetInt32() : 1, Is.EqualTo(1));
        var png = File.ReadAllBytes(Path.Combine(path, name + ".png"));
        Assert.That(Dimension(png, 16), Is.EqualTo(32));
        Assert.That(Dimension(png, 20), Is.EqualTo(32));
    }

    [Test]
    public void DripsImportAllStatesIncludingRewindingAnimations()
    {
        var path = Path.Combine(Resources, "Textures/_radiant/Structures/Medical/iv_drip.rsi");
        using var metadata = JsonDocument.Parse(File.ReadAllText(Path.Combine(path, "meta.json")));
        var states = metadata.RootElement.GetProperty("states").EnumerateArray().ToArray();
        Assert.That(states.Length, Is.EqualTo(21));
        foreach (var state in states)
        {
            var frames = state.TryGetProperty("delays", out var delays) ? delays[0].GetArrayLength() : 1;
            var png = File.ReadAllBytes(Path.Combine(path, state.GetProperty("name").GetString() + ".png"));
            Assert.That(Dimension(png, 16), Is.EqualTo(32 * frames));
            Assert.That(Dimension(png, 20), Is.EqualTo(32));
            if (state.GetProperty("name").GetString() is "injecting" or "donating")
                Assert.That(frames, Is.EqualTo(6));
        }
    }

    [Test]
    public void DripsHaveOnlyDecorativeComponents()
    {
        using var reader = File.OpenText(Path.Combine(Resources,
            "Prototypes/_radiant/Entities/Structures/Medical/iv_drip.yml"));
        var yaml = new YamlStream();
        yaml.Load(reader);
        var entities = ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>().ToArray();
        Assert.That(entities.Length, Is.EqualTo(2));
        foreach (var entity in entities)
        {
            var components = (YamlSequenceNode) entity.Children[new YamlScalarNode("components")];
            foreach (var component in components.Children.Cast<YamlMappingNode>())
                Assert.That(((YamlScalarNode) component.Children[new YamlScalarNode("type")]).Value,
                    Is.AnyOf("Sprite", "InteractionOutline", "Fixtures"));
        }
    }

    private static int Dimension(byte[] bytes, int start) =>
        bytes[start] << 24 | bytes[start + 1] << 16 | bytes[start + 2] << 8 | bytes[start + 3];
}

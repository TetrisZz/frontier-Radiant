using System.IO;
using System.Linq;
using System.Text.Json;
using Content.Shared._radiant.Paperwork;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

public sealed class PhotocopierConfigurationTest
{
    private static string Resources => Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", "..", "Resources"));

    private static YamlSequenceNode ReadPrototypes(string path)
    {
        using var reader = File.OpenText(Path.Combine(Resources, "Prototypes", path));
        var yaml = new YamlStream();
        yaml.Load(reader);
        return (YamlSequenceNode) yaml.Documents[0].RootNode;
    }

    private static string Value(YamlMappingNode node, string key) => node.Children[new YamlScalarNode(key)].ToString();

    private static YamlMappingNode Component(string type)
    {
        var copier = ReadPrototypes("_radiant/Entities/Structures/Machines/photocopier.yml").Children.Cast<YamlMappingNode>()
            .Single(node => Value(node, "id") == "Photocopier");
        return ((YamlSequenceNode) copier.Children[new YamlScalarNode("components")]).Children.Cast<YamlMappingNode>()
            .Single(node => Value(node, "type") == type);
    }

    [Test]
    public void DefaultCatalogComesFromTheMachinePrototypeAndOmitsSeparatistForms()
    {
        var forms = ((YamlSequenceNode) Component("Photocopier").Children[new YamlScalarNode("forms")]).Children
            .Select(node => node.ToString()).ToArray();
        Assert.That(forms.Distinct().Count(), Is.EqualTo(forms.Length));
        var catalog = ReadPrototypes("_radiant/Paperwork/forms.yml").Children.Cast<YamlMappingNode>().ToArray();
        var separatists = catalog.Where(node => Value(node, "category") == "document-category-separatists").ToArray();
        Assert.That(separatists, Has.Length.EqualTo(2), "Hidden templates remain available for explicitly configured custom machines.");
        Assert.That(forms, Is.SubsetOf(catalog.Except(separatists).Select(node => Value(node, "id"))),
            "The configured subset may be reduced without changing code or the catalog definitions.");
    }

    [Test]
    public void EmptyComponentCatalogDoesNotImplicitlyEnableEveryTemplate()
    {
        var first = new PhotocopierComponent();
        var second = new PhotocopierComponent();
        Assert.That(first.Forms, Is.Empty);
        first.Forms.Add("GeneralDocument");
        Assert.That(second.Forms, Is.Empty, "Machine catalogs must not share a mutable list.");
    }

    [Test]
    public void NativePrintingAnimationHasFortyFramesAndMatchesThePageDuration()
    {
        var copier = Component("Photocopier");
        using var json = JsonDocument.Parse(File.ReadAllText(Path.Combine(Resources, "Textures/_radiant/Structures/Machines/photocopier.rsi/meta.json")));
        var printing = json.RootElement.GetProperty("states").EnumerateArray().Single(state => state.GetProperty("name").GetString() == "photocopier1");
        var delays = printing.GetProperty("delays")[0].EnumerateArray().Select(delay => delay.GetDouble()).ToArray();
        Assert.That(delays, Has.Length.EqualTo(40));
        Assert.That(delays, Is.All.EqualTo(0.05));
        Assert.That(delays.Sum(), Is.EqualTo(double.Parse(Value(copier, "printDuration"), System.Globalization.CultureInfo.InvariantCulture)).Within(0.00001));
        Assert.That(File.Exists(Path.Combine(Resources, "Textures/_radiant/Structures/Machines/photocopier.rsi/photocopier1.png")), Is.True);
        var visuals = (YamlMappingNode) Component("GenericVisualizer").Children[new YamlScalarNode("visuals")];
        var states = (YamlMappingNode) ((YamlMappingNode) visuals.Children[new YamlScalarNode("enum.PhotocopierVisuals.Printing")])
            .Children[new YamlScalarNode("body")];
        Assert.That(Value((YamlMappingNode) states.Children[new YamlScalarNode("True")], "state"), Is.EqualTo("photocopier1"));
        Assert.That(Value((YamlMappingNode) states.Children[new YamlScalarNode("False")], "state"), Is.EqualTo("photocopier"));
    }
}

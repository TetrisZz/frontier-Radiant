using System.Linq;
using System.IO;
using Content.Shared._radiant.Passports;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class ServiceCredentialJobsTest
{
    [TestCase("dvb_closed")]
    [TestCase("fleet_closed")]
    public void CredentialHasItsOwnTransparentSizedSprite(string state)
    {
        var root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", ".."));
        var folder = Path.Combine(root, "Resources", "Textures", "_radiant", "Objects", "Documents", "ServiceCredentials.rsi");
        using var metadata = System.Text.Json.JsonDocument.Parse(File.ReadAllText(Path.Combine(folder, "meta.json")));
        Assert.That(metadata.RootElement.GetProperty("size").GetProperty("x").GetInt32(), Is.EqualTo(32));
        Assert.That(metadata.RootElement.GetProperty("states").EnumerateArray()
            .Any(entry => entry.GetProperty("name").GetString() == state), Is.True);
        var png = File.ReadAllBytes(Path.Combine(folder, state + ".png"));
        Assert.That(png[19], Is.EqualTo(32)); // PNG IHDR width; all higher bytes are zero.
        Assert.That(png[23], Is.EqualTo(32));
        Assert.That(png[25], Is.EqualTo(6)); // RGBA, not an opaque RGB image.
    }

    [TestCase(6)]
    [TestCase(8)]
    [TestCase(12)]
    public void NumbersAreNumericAndHaveTheSpecifiedLength(int digits)
    {
        for (var i = 0; i < 100; i++)
        {
            var number = ServiceCredentialNumbers.Create(digits);
            Assert.That(number.Length, Is.EqualTo(digits));
            Assert.That(number.All(c => c is >= '0' and <= '9'), Is.True);
        }
    }

    [Test]
    public void EachEligibleJobHasItsOwnCredential()
    {
        var prototypes = new System.Collections.Generic.HashSet<string>();
        foreach (var job in ServiceCredentialJobs.Dvb.Concat(ServiceCredentialJobs.Fleet))
        {
            Assert.That(ServiceCredentialJobs.TryGet(job, out var service, out var prototype), Is.True);
            Assert.That(service, Is.EqualTo(ServiceCredentialJobs.Dvb.Contains(job)
                ? CredentialService.Dvb : CredentialService.Fleet));
            Assert.That(prototypes.Add(prototype), Is.True, job);
        }
        Assert.That(prototypes.Count, Is.EqualTo(12));
    }

    [TestCase(null)]
    [TestCase("SecurityOfficer")]
    [TestCase("Mercenary")]
    [TestCase("Captain")]
    [TestCase("Prosecutor")]
    public void OtherJobsDoNotReceiveServiceCredentials(string job)
        => Assert.That(ServiceCredentialJobs.TryGet(job, out _, out _), Is.False);

    [Test]
    public void IssuanceCatalogMatchesEntityPrototypes()
    {
        var root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", ".."));
        using var reader = File.OpenText(Path.Combine(root, "Resources", "Prototypes", "_radiant", "Dossiers", "service_credentials.yml"));
        var yaml = new YamlStream();
        yaml.Load(reader);
        var entities = ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>()
            .ToDictionary(node => ((YamlScalarNode) node.Children[new YamlScalarNode("id")]).Value!);
        foreach (var job in ServiceCredentialJobs.Dvb.Concat(ServiceCredentialJobs.Fleet))
        {
            ServiceCredentialJobs.TryGet(job, out var service, out var prototype);
            Assert.That(entities.ContainsKey(prototype), Is.True, prototype);
            var entity = entities[prototype];
            var expectedParent = service == CredentialService.Dvb ? "BaseRadiantCredentialDvb" : "BaseRadiantCredentialFleet";
            Assert.That(((YamlScalarNode) entity.Children[new YamlScalarNode("parent")]).Value, Is.EqualTo(expectedParent));
            var component = ((YamlSequenceNode) entity.Children[new YamlScalarNode("components")]).Children.Cast<YamlMappingNode>()
                .Single(node => ((YamlScalarNode) node.Children[new YamlScalarNode("type")]).Value == "ServiceCredential");
            Assert.That(((YamlScalarNode) component.Children[new YamlScalarNode("jobId")]).Value, Is.EqualTo(job));
        }
    }
}

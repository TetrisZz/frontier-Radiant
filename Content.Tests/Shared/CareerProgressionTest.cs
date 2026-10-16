using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using Content.Shared._radiant.Careers;
using NUnit.Framework;
using YamlDotNet.RepresentationModel;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class CareerProgressionTest
{
    private static CareerProfessionPrototype Profession() => new()
    {
        JobTitles = new()
        {
            new CareerJobTitle { Id = "basic", Name = "career-title-doctor" },
            new CareerJobTitle { Id = "advanced", Name = "career-title-surgeon", RequiredLevel = 4 },
        },
    };

    [TestCase(0, false)]
    [TestCase(3, false)]
    [TestCase(4, true)]
    [TestCase(6, true)]
    public void TitlesUnlockAfterCompletingTheRequiredStage(int level, bool unlocked)
    {
        var profession = Profession();
        var career = new CareerData { Level = level, JobTitle = "advanced" };
        Assert.That(CareerTitleRules.IsUnlocked(profession.JobTitles[1], career), Is.EqualTo(unlocked));
        Assert.That(CareerTitleRules.GetSelected(profession, career)?.Id, Is.EqualTo(unlocked ? "advanced" : "basic"));
    }

    [TestCase(null)]
    [TestCase("unknown")]
    public void MissingAndInvalidTitlesFallBackToTheEntryTitle(string title)
    {
        Assert.That(CareerTitleRules.GetSelected(Profession(), new CareerData { JobTitle = title })?.Id,
            Is.EqualTo("basic"));
        Assert.That(CareerTitleRules.GetSelected(new CareerProfessionPrototype(), new CareerData()), Is.Null);
    }

    [Test]
    public void EarlierTitlesRemainSelectableAndSelectionSurvivesSaving()
    {
        var career = new CareerData { CharacterId = Guid.NewGuid(), Level = 6, JobTitle = "basic" };
        Assert.That(CareerTitleRules.GetSelected(Profession(), career)?.Id, Is.EqualTo("basic"));
        Assert.That(career.Clone().JobTitle, Is.EqualTo("basic"));
        var options = new JsonSerializerOptions { IncludeFields = true };
        var saved = JsonSerializer.Deserialize<CareerData>(JsonSerializer.Serialize(career, options), options);
        Assert.That(saved.JobTitle, Is.EqualTo("basic"));
        Assert.That(saved.CharacterId, Is.EqualTo(career.CharacterId));
        Assert.That(JsonSerializer.Deserialize<CareerData>("{\"Level\":2}", options).JobTitle, Is.Null);
    }

    [TestCase("engineering.yml", "Engineering", 3)]
    [TestCase("medical.yml", "Medical", 6)]
    [TestCase("professions.yml", "Mining", 3)]
    public void CareerStagesHaveMoneyAndValidTitleUnlocks(string file, string professionId, int count)
    {
        var root = Path.GetFullPath(Path.Combine(TestContext.CurrentContext.TestDirectory, "..", ".."));
        using var reader = File.OpenText(Path.Combine(root, "Resources", "Prototypes", "_radiant", "Careers", file));
        var yaml = new YamlStream();
        yaml.Load(reader);
        var nodes = ((YamlSequenceNode) yaml.Documents[0].RootNode).Children.Cast<YamlMappingNode>()
            .ToDictionary(node => Scalar(node, "id"));
        var profession = nodes[professionId];
        var levels = (YamlSequenceNode) profession.Children[new YamlScalarNode("levels")];
        Assert.That(levels.Children.Count, Is.EqualTo(count));
        for (var i = 0; i < count; i++)
            Assert.That(Scalar(nodes[((YamlScalarNode) levels.Children[i]).Value], "rewardMoney"),
                Is.EqualTo(((i + 1) * 10000).ToString()));

        var titles = ((YamlSequenceNode) profession.Children[new YamlScalarNode("jobTitles")])
            .Children.Cast<YamlMappingNode>().ToArray();
        Assert.That(titles.Select(title => Scalar(title, "id")).Distinct().Count(), Is.EqualTo(titles.Length));
        Assert.That(titles[0].Children.ContainsKey(new YamlScalarNode("requiredLevel")), Is.False);
        foreach (var title in titles.Skip(1))
            Assert.That(int.Parse(Scalar(title, "requiredLevel")), Is.InRange(1, count));
    }

    private static string Scalar(YamlMappingNode node, string key) =>
        ((YamlScalarNode) node.Children[new YamlScalarNode(key)]).Value;
}

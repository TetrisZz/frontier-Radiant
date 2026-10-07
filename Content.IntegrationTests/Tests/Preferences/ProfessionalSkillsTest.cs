using System.Linq;
using Content.Shared._radiant.Skills;
using Content.Shared.Preferences;

namespace Content.IntegrationTests.Tests.Preferences;

[TestFixture]
public sealed class ProfessionalSkillsTest
{
    [Test]
    public void NormalizationIsBoundedAndStable()
    {
        var source = new[] { 100, -1, 100, 100, 100, 100, 100, 100, 100 };
        var levels = ProfessionalSkillRules.Normalize(source);
        Assert.That(levels.Length, Is.EqualTo(8));
        Assert.That(levels.Sum(), Is.EqualTo(10));
        Assert.That(levels[1], Is.Zero);
        for (var i = 0; i < levels.Length; i++)
            Assert.That(levels[i], Is.InRange(0, ProfessionalSkillRules.Maximum((ProfessionalSkill) i)));
        Assert.That(ProfessionalSkillRules.Normalize(levels), Is.EqualTo(levels));
        Assert.That(source[0], Is.EqualTo(100));
        Assert.That(ProfessionalSkillRules.Normalize(null), Is.EqualTo(new int[8]));
    }

    [Test]
    public void EveryLevelCostsOneAndCannotOverspend()
    {
        var original = new HumanoidCharacterProfile();
        var profile = original.WithSkillLevel(ProfessionalSkill.Medicine, 4)
            .WithSkillLevel(ProfessionalSkill.Engineering, 3)
            .WithSkillLevel(ProfessionalSkill.Salvage, 3);
        Assert.That(profile.SkillPointsSpent, Is.EqualTo(10));
        Assert.That(profile.WithSkillLevel(ProfessionalSkill.Piloting, 1), Is.SameAs(profile));
        Assert.That(profile.WithSkillLevel(ProfessionalSkill.Medicine, 5), Is.SameAs(profile));
        Assert.That(profile.WithSkillLevel(ProfessionalSkill.Medicine, -1), Is.SameAs(profile));
        var reduced = profile.WithSkillLevel(ProfessionalSkill.Medicine, 3);
        Assert.That(reduced.SkillPointsSpent, Is.EqualTo(9));
        Assert.That(reduced.WithSkillLevel(ProfessionalSkill.Piloting, 1).SkillPointsSpent, Is.EqualTo(10));
        Assert.That(original.SkillPointsSpent, Is.Zero);
    }

    [Test]
    public void CopiesDoNotShareSkillArrays()
    {
        var profile = new HumanoidCharacterProfile().WithSkillLevel(ProfessionalSkill.Medicine, 3);
        var clone = profile.Clone();
        Assert.That(clone.SkillLevels, Is.EqualTo(profile.SkillLevels));
        Assert.That(clone.SkillLevels, Is.Not.SameAs(profile.SkillLevels));
        Assert.That(profile.WithName("Test").SkillLevels, Is.EqualTo(profile.SkillLevels));
    }

    [TestCase("MachineFrame", 1)]
    [TestCase("IntegratedCircuit", 1)]
    [TestCase("GasUnary", 2)]
    [TestCase("AmeShielding", 3)]
    [TestCase("TeslaGeneratorMachineCircuitboard", 3)]
    [TestCase("TEG", 3)]
    public void ConstructionCategoriesDoNotConfuseFramesWithAme(string graph, int expected)
    {
        Assert.That(SharedProfessionalSkillsSystem.ConstructionLevel(graph), Is.EqualTo(expected));
    }

    [TestCase(0, 1)]
    [TestCase(2, 1)]
    [TestCase(3, 2)]
    [TestCase(4, 2)]
    [TestCase(5, 3)]
    public void CookingComplexity(int ingredients, int expected)
    {
        Assert.That(ProfessionalSkillRules.CookingLevel(ingredients), Is.EqualTo(expected));
    }
}

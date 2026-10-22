using Content.Shared._radiant.Passports;
using Content.Shared._radiant.Skills;
using Content.Shared.Preferences;
using NUnit.Framework;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class CharacterSelectionRulesTest
{
    [Test]
    public void OnePointIsEnoughAndEmptyOrInvalidLevelsDoNotQualify()
    {
        Assert.That(ProfessionalSkillRules.CanJoin(null), Is.False);
        Assert.That(ProfessionalSkillRules.CanJoin([]), Is.False);
        Assert.That(ProfessionalSkillRules.CanJoin(new int[ProfessionalSkillRules.Count]), Is.False);
        Assert.That(ProfessionalSkillRules.CanJoin([-1]), Is.False);
        Assert.That(ProfessionalSkillRules.CanJoin([0, 1]), Is.True);
        Assert.That(ProfessionalSkillRules.CanJoin([3, 3, 4]), Is.True);
        var profile = new HumanoidCharacterProfile();
        Assert.That(ProfessionalSkillRules.CanJoin(profile.SkillLevels), Is.False);
        profile = profile.WithSkillLevel(ProfessionalSkill.Botany, 1);
        Assert.That(profile.SkillPointsSpent, Is.EqualTo(1));
        Assert.That(ProfessionalSkillRules.CanJoin(profile.SkillLevels), Is.True);
    }

    [Test]
    public void NewProfilesUseEnoraAndExistingCitizenshipRemainsEditable()
    {
        var profile = new HumanoidCharacterProfile();
        Assert.That(profile.Citizenship, Is.EqualTo(RadiantCitizenship.Zon));
        profile = profile.WithCitizenship(RadiantCitizenship.Hesperia);
        Assert.That(profile.Citizenship, Is.EqualTo(RadiantCitizenship.Hesperia));
        profile = profile.WithCitizenship(RadiantCitizenship.Aurum);
        Assert.That(profile.Citizenship, Is.EqualTo(RadiantCitizenship.Aurum));
    }

    [TestCase(null, RadiantCitizenship.Zon)]
    [TestCase("Unknown", RadiantCitizenship.Zon)]
    [TestCase("Confederation", RadiantCitizenship.Zon)]
    [TestCase("255", RadiantCitizenship.Zon)]
    [TestCase("Zon", RadiantCitizenship.Zon)]
    [TestCase("Locked:Hesperia", RadiantCitizenship.Hesperia)]
    [TestCase("Locked:NT", RadiantCitizenship.NT)]
    public void LegacyCitizenshipStringsRemainReadable(string? value, RadiantCitizenship expected)
        => Assert.That(RadiantCitizenships.Parse(value), Is.EqualTo(expected));
}

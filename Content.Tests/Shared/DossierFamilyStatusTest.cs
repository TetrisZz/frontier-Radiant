using Content.Shared._radiant.Dossiers;
using Content.Shared.Humanoid;
using NUnit.Framework;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class DossierFamilyStatusTest
{
    [TestCase("Женат", "married")]
    [TestCase("Замужем", "married")]
    [TestCase("Холост", "single")]
    [TestCase("не замужем", "single")]
    [TestCase("в отношениях", "partnered")]
    [TestCase("anything else", "")]
    public void ExistingTextIsNormalized(string input, string expected)
        => Assert.That(DossierFamilyStatus.Normalize(input), Is.EqualTo(expected));

    [Test]
    public void MarriedDisplayDependsOnSex()
    {
        Assert.That(DossierFamilyStatus.LocalizationKey("married", Sex.Male),
            Is.EqualTo("radiant-dossier-family-married-male"));
        Assert.That(DossierFamilyStatus.LocalizationKey("married", Sex.Female),
            Is.EqualTo("radiant-dossier-family-married-female"));
    }
}

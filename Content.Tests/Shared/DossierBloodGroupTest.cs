using Content.Shared._radiant.Dossiers;
using NUnit.Framework;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class DossierBloodGroupTest
{
    [Test]
    public void EveryRollProducesSupportedGroup()
    {
        for (var value = 0; value < 100; value++)
            Assert.That(DossierBloodGroup.IsValid(DossierBloodGroup.Roll(value)), Is.True);
    }

    [Test]
    public void EmptyOrInventedGroupIsRejected()
    {
        Assert.That(DossierBloodGroup.IsValid(""), Is.False);
        Assert.That(DossierBloodGroup.IsValid("X+"), Is.False);
    }
}

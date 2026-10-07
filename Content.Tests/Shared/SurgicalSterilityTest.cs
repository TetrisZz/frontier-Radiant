using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class SurgicalSterilityTest
{
    private readonly EntityUid _first = new(1);
    private readonly EntityUid _second = new(2);

    [TestCase(0, 0, false, 0)]
    [TestCase(0, 0, true, -5)]
    [TestCase(1, 1, false, 5)]
    [TestCase(1, 1, true, 0)]
    [TestCase(100, 100, false, 20)]
    [TestCase(100, 100, true, 15)]
    public void EnvironmentRiskIsCapped(int trash, int puddles, bool sterilizer, int expected)
        => Assert.That(SurgicalSterilityRules.EnvironmentRisk(trash, puddles, sterilizer), Is.EqualTo(expected));

    [TestCase(0, true, 0)]
    [TestCase(3, true, 0)]
    [TestCase(15, true, 10)]
    [TestCase(15, false, 15)]
    public void DrapeMitigatesNewRiskWithoutNegativeContamination(int incoming, bool draped, int expected)
        => Assert.That(SurgicalSterilityRules.ApplyFieldProtection(incoming, draped), Is.EqualTo(expected));

    [TestCase(true, true, false, 0)]
    [TestCase(true, true, true, 5)]
    [TestCase(false, true, false, 14)]
    [TestCase(true, false, false, 7)]
    [TestCase(false, false, false, 21)]
    [TestCase(false, false, true, 26)]
    public void EquipmentBonusesAndPenalties(bool gloves, bool mask, bool outerwear, int expected)
        => Assert.That(SurgicalSterilityRules.EquipmentRisk(gloves, mask, outerwear), Is.EqualTo(expected));

    [TestCase(6, 0)]
    [TestCase(6, 25)]
    [TestCase(10, 0)]
    [TestCase(10, 100)]
    public void ToolsAndGlovesStaySafeForTheSamePatient(int limit, int contamination)
    {
        var item = new SurgicalItemSterilityComponent();
        for (var i = 0; i < 100; i++)
        {
            Assert.That(SurgicalSterilityRules.Contact(item, _first, contamination, true, limit), Is.Zero);
            Assert.That(item.Dirty, Is.False);
        }
        Assert.That(SurgicalSterilityRules.Contact(item, _second, 0, true, limit), Is.EqualTo(15));
        Assert.That(item.Dirty, Is.True);
        Assert.That(SurgicalSterilityRules.Contact(item, _second, 0, true), Is.EqualTo(15));
        SurgicalSterilityRules.Disinfect(item);
        Assert.That(item.UsesSinceCleaning, Is.Zero);
        Assert.That(SurgicalSterilityRules.Contact(item, _second, 0, true), Is.Zero);
        Assert.That(item.Dirty, Is.False);
    }

    [Test]
    public void CleanToolCanBeReusedOnSamePatientButNotAnother()
    {
        var item = new SurgicalItemSterilityComponent();
        Assert.That(SurgicalSterilityRules.Contact(item, _first, 0), Is.Zero);
        Assert.That(SurgicalSterilityRules.Contact(item, _first, 0), Is.Zero);
        Assert.That(SurgicalSterilityRules.Contact(item, _second, 0), Is.EqualTo(15));
        Assert.That(SurgicalSterilityRules.Contact(item, _second, 0), Is.EqualTo(15));
    }

    [Test]
    public void DirtyToolContaminatesEvenItsPreviousPatient()
    {
        var item = new SurgicalItemSterilityComponent { Dirty = true, Used = true, LastPatient = _first };
        Assert.That(SurgicalSterilityRules.Contact(item, _first, 0), Is.EqualTo(15));
        SurgicalSterilityRules.Disinfect(item);
        Assert.That(item.LastPatient, Is.Null);
        Assert.That(SurgicalSterilityRules.Contact(item, _second, 0), Is.Zero);
    }

    [Test]
    public void ContaminatedWoundDirtiesCleanTool()
    {
        var item = new SurgicalItemSterilityComponent();
        SurgicalSterilityRules.Contact(item, _first, 25);
        Assert.That(item.Dirty, Is.True);
        Assert.That(SurgicalSterilityRules.Contact(item, _first, 0), Is.EqualTo(15));
    }

    [Test]
    public void CavitiesRemainIndependentAndValuesAreBounded()
    {
        var wound = new SurgicalSterilityComponent();
        wound.Contamination[SurgicalSite.Groin] = 0;
        SurgicalSterilityRules.AddContamination(wound, SurgicalSite.Ribcage, 15);
        SurgicalSterilityRules.AddContamination(wound, SurgicalSite.Abdomen, 200);
        Assert.That(wound.Contamination[SurgicalSite.Ribcage], Is.EqualTo(25));
        Assert.That(wound.Contamination[SurgicalSite.Abdomen], Is.EqualTo(100));
        Assert.That(wound.Contamination[SurgicalSite.Groin], Is.Zero);
        wound.Contamination[SurgicalSite.Ribcage] = 0;
        Assert.That(wound.Contamination[SurgicalSite.Abdomen], Is.EqualTo(100));
    }
}

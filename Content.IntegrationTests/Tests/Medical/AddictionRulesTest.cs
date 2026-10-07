using Content.Shared._radiant.Addictions;

namespace Content.IntegrationTests.Tests.Medical;

[TestFixture]
public sealed class AddictionRulesTest
{
    [Test]
    public void SmallDoseDoesNotImmediatelyCreateDependence()
    {
        var state = new AddictionState();
        AddictionRules.Dose(state, new AddictionGroupPrototype(), 5);
        Assert.That(state.Dependence, Is.LessThan(20));
        Assert.That(state.Tolerance, Is.GreaterThan(0));
    }

    [Test]
    public void RepeatedDosesBuildGroupDependence()
    {
        var state = new AddictionState();
        var group = new AddictionGroupPrototype();
        for (var i = 0; i < 100; i++)
            AddictionRules.Dose(state, group, 1);
        Assert.That(state.Dependence, Is.EqualTo(100));
        Assert.That(state.Tolerance, Is.InRange(90, 100));
    }

    [Test]
    public void ToleranceDoesNotProtectAgainstToxicLoad()
    {
        var naive = new AddictionState();
        var tolerant = new AddictionState { Tolerance = 100 };
        var group = new AddictionGroupPrototype();
        AddictionRules.Dose(naive, group, 10);
        AddictionRules.Dose(tolerant, group, 10);
        Assert.That(tolerant.Intoxication, Is.EqualTo(naive.Intoxication));
        Assert.That(AddictionRules.Strength(100), Is.EqualTo(.55f).Within(.001));
    }

    [Test]
    public void WithdrawalRecoveryAndReliefAreSeparate()
    {
        var group = new AddictionGroupPrototype();
        var state = new AddictionState { Dependence = 100, Tolerance = 100 };
        AddictionRules.Advance(state, group, 299, false);
        Assert.That(state.WithdrawalStage, Is.Zero);
        AddictionRules.Advance(state, group, 2, false);
        Assert.That(state.WithdrawalStage, Is.EqualTo(1));
        var before = state.Dependence;
        AddictionRules.Dose(state, group, 1);
        Assert.That(state.WithdrawalStage, Is.Zero);
        Assert.That(state.Dependence, Is.GreaterThanOrEqualTo(before));
        AddictionRules.Advance(state, group, 3000, true);
        Assert.That(state.Tolerance, Is.LessThan(state.Dependence).Or.EqualTo(0));
    }
}

using Content.Shared.Chat.Prototypes;
using NUnit.Framework;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class InteractionArousalTest
{
    [TestCase(-1, 0)]
    [TestCase(0, 0)]
    [TestCase(1, 0)]
    [TestCase(2, 1)]
    [TestCase(3, 1)]
    [TestCase(4, 2)]
    [TestCase(5, 2)]
    public void OrdinaryInteractionsGiveHalfArousal(int configured, int expected)
    {
        var action = new InteractionPrototype { ArousalPoints = configured };
        Assert.That(action.EffectiveArousal, Is.EqualTo(expected));
        action.ArousalPoints = 0;
        action.Points = configured;
        Assert.That(action.EffectiveArousal, Is.EqualTo(expected));
    }

    [TestCase(1)]
    [TestCase(2)]
    [TestCase(5)]
    public void ExplicitInteractionsKeepTheirConfiguredGain(int configured)
    {
        var action = new InteractionPrototype { ERP = true, ArousalPoints = configured };
        Assert.That(action.EffectiveArousal, Is.EqualTo(configured));
    }
}

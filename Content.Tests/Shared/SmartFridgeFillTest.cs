using Content.Shared._radiant.SmartFridge;
using NUnit.Framework;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class SmartFridgeFillTest
{
    [TestCase(0, 0)]
    [TestCase(1, 1)]
    [TestCase(25, 1)]
    [TestCase(26, 2)]
    [TestCase(75, 2)]
    [TestCase(76, 3)]
    [TestCase(300, 3)]
    public void ContentsSelectOriginalFillThresholds(int count, int expected)
    {
        Assert.That(SmartFridgeFillVisualsComponent.FillLevel(count), Is.EqualTo(expected));
    }
}

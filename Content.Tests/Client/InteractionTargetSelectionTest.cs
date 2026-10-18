using System.Numerics;
using Content.Client.Interaction.Panel.Ui;
using NUnit.Framework;
using Robust.Shared.GameObjects;
using Robust.Shared.Map;

namespace Content.Tests.Client;

[TestFixture]
public sealed class InteractionTargetSelectionTest
{
    [TestCase(0f, 0f, true)]
    [TestCase(1f, 1f, true)]
    [TestCase(2f, 0f, true)]
    [TestCase(2.01f, 0f, false)]
    [TestCase(1.5f, 1.5f, false)]
    [TestCase(6f, 0f, false)]
    public void SearchUsesActualTwoTileDistance(float x, float y, bool expected)
    {
        var origin = new MapCoordinates(Vector2.Zero, new MapId(1));
        var target = new MapCoordinates(new Vector2(x, y), new MapId(1));
        Assert.That(InteractionTargetSelection.InRange(origin, target), Is.EqualTo(expected));
    }

    [Test]
    public void OtherMapsAndNullspaceAreNeverNearby()
    {
        var origin = new MapCoordinates(Vector2.Zero, new MapId(1));
        Assert.That(InteractionTargetSelection.InRange(origin, new MapCoordinates(Vector2.Zero, new MapId(2))), Is.False);
        Assert.That(InteractionTargetSelection.InRange(MapCoordinates.Nullspace, MapCoordinates.Nullspace), Is.False);
    }

    [Test]
    public void ManualSelectionDoesNotSwitchWhenAnotherCharacterIsCloserOrPartnerLeaves()
    {
        var closest = new EntityUid(1);
        var partner = new EntityUid(2);
        EntityUid[] nearby = [closest, partner];
        Assert.That(InteractionTargetSelection.Resolve(null, nearby), Is.EqualTo(closest));
        Assert.That(InteractionTargetSelection.Resolve(partner, nearby), Is.EqualTo(partner));
        Assert.That(InteractionTargetSelection.Resolve(partner, [closest]), Is.Null);
        Assert.That(InteractionTargetSelection.Resolve(null, []), Is.Null);
    }

    [TestCase(1f, true, true, InteractionTargetStatus.Ready)]
    [TestCase(1f, true, false, InteractionTargetStatus.Obstructed)]
    [TestCase(3f, true, false, InteractionTargetStatus.TooFar)]
    [TestCase(1f, false, true, InteractionTargetStatus.Unavailable)]
    public void TargetStatusSeparatesDistanceObstaclesAndInvalidCharacters(float distance, bool valid, bool unobstructed, InteractionTargetStatus expected)
    {
        var user = new MapCoordinates(Vector2.Zero, new MapId(1));
        var target = new MapCoordinates(new Vector2(distance, 0), new MapId(1));
        Assert.That(InteractionTargetSelection.Status(user, target, valid, unobstructed), Is.EqualTo(expected));
        Assert.That(InteractionTargetSelection.Status(user, null, false, false), Is.EqualTo(InteractionTargetStatus.None));
    }
}

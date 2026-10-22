using System;
using System.Reflection;
using Content.Shared._radiant.Fishing.Components;
using Content.Shared._radiant.Fishing.Systems;
using Content.Shared.Damage;
using Content.Shared.FixedPoint;
using Moq;
using NUnit.Framework;
using Robust.Shared.Analyzers;
using Robust.Shared.GameObjects;
using Robust.Shared.GameStates;
using Robust.Shared.Timing;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class FishRespirationTest
{
    [TestCase(true, -1, "Asphyxiation", 1, false, true)]
    [TestCase(false, 120, "Asphyxiation", 1, false, true)]
    [TestCase(false, -1, "Asphyxiation", 1, false, false)]
    [TestCase(false, 0, "Asphyxiation", 1, false, false)]
    [TestCase(true, -1, "Asphyxiation", -1, false, false)]
    [TestCase(true, -1, "Asphyxiation", 1, true, false)]
    [TestCase(true, -1, "Poison", 1, false, false)]
    [TestCase(true, -1, "Blunt", 1, false, false)]
    [TestCase(true, -1, "Mixed", 1, false, false)]
    public void SharedDamageGuardOnlyBlocksAmbientSuffocation(bool inWater, int graceSeconds,
        string damageType, int amount, bool hasOrigin, bool cancelled)
    {
        var now = TimeSpan.FromSeconds(500);
        var timing = new Mock<IGameTiming>();
        timing.SetupGet(t => t.CurTime).Returns(now);
        var system = new FishRespirationSystem();
        typeof(FishRespirationSystem).GetField("_timing", BindingFlags.Instance | BindingFlags.NonPublic)!
            .SetValue(system, timing.Object);
        var fish = new FishComponent
        {
            Owner = new EntityUid(1),
            InAquariumWater = inWater,
            AirGraceEnds = now + TimeSpan.FromSeconds(graceSeconds),
        };
        var damage = new DamageSpecifier();
        if (damageType == "Mixed")
        {
            damage.DamageDict["Asphyxiation"] = FixedPoint2.New(amount);
            damage.DamageDict["Blunt"] = FixedPoint2.New(amount);
        }
        else
            damage.DamageDict[damageType] = FixedPoint2.New(amount);
        var before = new BeforeDamageChangedEvent(damage, hasOrigin ? new EntityUid(2) : null);
        var args = new object[] { new Entity<FishComponent>(new EntityUid(1), fish), before };
        // Exercise the handler used by both client prediction and authoritative server damage.
        typeof(FishRespirationSystem).GetMethod("OnDamage", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(system, args);
        Assert.That(((BeforeDamageChangedEvent) args[1]).Cancelled, Is.EqualTo(cancelled));
    }

    [Test]
    public void ClientReceivesBothWaterStateAndTransportDeadline()
    {
        Assert.That(Attribute.IsDefined(typeof(FishComponent), typeof(NetworkedComponentAttribute)), Is.True);
        foreach (var name in new[] { nameof(FishComponent.InAquariumWater), nameof(FishComponent.AirGraceEnds) })
            Assert.That(Attribute.IsDefined(typeof(FishComponent).GetField(name)!, typeof(AutoNetworkedFieldAttribute)), Is.True, name);
    }
}

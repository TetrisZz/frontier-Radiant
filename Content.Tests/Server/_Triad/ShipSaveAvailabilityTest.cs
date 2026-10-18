using System;
using Content.Server._Triad.Shipyard;
using NUnit.Framework;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Tests.Server._Triad;

[TestFixture]
public sealed class ShipSaveAvailabilityTest
{
    [Test]
    public void DeploymentBlocksDuplicatesButCanRecoverAfterRestart()
    {
        var files = new VirtualWritableDirProvider();
        var running = new ShipSaveAvailabilityStore(files, Guid.NewGuid());
        Assert.That(running.IsAvailable("ship"), Is.False);
        Assert.That(running.IsKnown("ship"), Is.False);
        running.MakeAvailable("ship");
        Assert.That(running.IsKnown("ship"), Is.True);
        Assert.That(running.IsAvailable("ship"), Is.True);
        running.Deploy("ship");
        Assert.That(running.IsAvailable("ship"), Is.False);
        var restarted = new ShipSaveAvailabilityStore(files, Guid.NewGuid());
        Assert.That(restarted.IsAvailable("ship"), Is.True);
        restarted.Deploy("ship");
        Assert.That(restarted.IsAvailable("ship"), Is.False);
        Assert.That(new ShipSaveAvailabilityStore(files, Guid.NewGuid()).IsAvailable("ship"), Is.True);
    }

    [Test]
    public void ReplacedAndSoldSnapshotsStayRevokedAfterRestart()
    {
        var files = new VirtualWritableDirProvider();
        var running = new ShipSaveAvailabilityStore(files, Guid.NewGuid());
        running.MakeAvailable("old");
        running.Deploy("old");
        running.MakeAvailable("new");
        running.Revoke("old");
        Assert.That(running.IsKnown("old"), Is.True, "Revoked files must not look like unknown legacy imports");
        var restarted = new ShipSaveAvailabilityStore(files, Guid.NewGuid());
        Assert.That(restarted.IsAvailable("old"), Is.False);
        Assert.That(restarted.IsAvailable("new"), Is.True);
        restarted.Deploy("new");
        restarted.Revoke("new");
        Assert.That(new ShipSaveAvailabilityStore(files, Guid.NewGuid()).IsAvailable("new"), Is.False);
    }

    [Test]
    public void FailedReplacementLeavesRecoverySnapshotIntact()
    {
        var files = new VirtualWritableDirProvider();
        var running = new ShipSaveAvailabilityStore(files, Guid.NewGuid());
        running.MakeAvailable("old");
        running.Deploy("old");
        running.MakeAvailable("failed");
        running.Revoke("failed");
        var restarted = new ShipSaveAvailabilityStore(files, Guid.NewGuid());
        Assert.That(restarted.IsAvailable("old"), Is.True);
        Assert.That(restarted.IsAvailable("failed"), Is.False);
    }

    [Test]
    public void CorruptDeploymentMarkerDoesNotEnableLoading()
    {
        var files = new VirtualWritableDirProvider();
        files.CreateDir(new ResPath("/Shipyard/available"));
        using (var stream = files.OpenWrite(new ResPath("/Shipyard/available/broken.bin")))
            stream.WriteByte(2);
        Assert.That(new ShipSaveAvailabilityStore(files, Guid.NewGuid()).IsAvailable("broken"), Is.False);
    }
}

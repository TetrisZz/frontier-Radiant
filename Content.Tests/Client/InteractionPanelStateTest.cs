using System.Linq;
using System.IO;
using System.Numerics;
using Content.Client.Interaction.Panel.Ui;
using Content.Shared.Chat.Prototypes;
using NUnit.Framework;
using Moq;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.Tests.Client;

[TestFixture]
public sealed class InteractionPanelStateTest
{
    [Test]
    public void ImportMergesCustomActionsWithoutRemovingOthersOrOverridingBuiltIns()
    {
        var original = new InteractionPrototype { ID = "Keep", Name = "Keep" };
        var old = new InteractionPrototype { ID = "Update", Name = "Old" };
        var updated = new InteractionPrototype { ID = "Update", Name = "New" };
        var added = new InteractionPrototype { ID = "Add", Name = "Add" };
        var result = InteractionCustomActions.Merge([original, old], [updated, added, new InteractionPrototype { ID = "BuiltIn" }],
            id => id == "BuiltIn", out var addedCount, out var updatedCount, out var skippedCount);
        Assert.That(result.Select(action => action.ID), Is.EquivalentTo(new[] { "Keep", "Update", "Add" }));
        Assert.That(result.Single(action => action.ID == "Update"), Is.SameAs(updated));
        Assert.That(addedCount, Is.EqualTo(1));
        Assert.That(updatedCount, Is.EqualTo(1));
        Assert.That(skippedCount, Is.EqualTo(1));
    }

    [Test]
    public void WindowPreferencesClampMalformedSizesAndScroll()
    {
        var prefs = new InteractionPanelPreferences { WindowSize = new Vector2(float.NaN, -10), Scroll = float.PositiveInfinity, Expanded = ["Hands"] };
        prefs.Sanitize();
        Assert.That(prefs.WindowSize, Is.EqualTo(new Vector2(660, 560)));
        Assert.That(prefs.Scroll, Is.Zero);
        Assert.That(prefs.Expanded, Is.EqualTo(new[] { "Hands" }));
    }

    [Test]
    public void SavingReplacesWholeFileAndKeepsPreviousVersionAsBackup()
    {
        var directory = new VirtualWritableDirProvider();
        var file = new ResPath("/actions.yml");
        InteractionPanelStorage.Write(directory, file, "original");
        InteractionPanelStorage.Write(directory, file, "new");
        Assert.That(directory.ReadAllText(file), Is.EqualTo("new"));
        Assert.That(directory.ReadAllText(new ResPath("/actions.yml.bak")), Is.EqualTo("original"));
        InteractionPanelStorage.Write(directory, file, "third");
        Assert.That(directory.ReadAllText(file), Is.EqualTo("third"));
        Assert.That(directory.ReadAllText(new ResPath("/actions.yml.bak")), Is.EqualTo("new"));
        Assert.That(directory.Exists(new ResPath("/actions.yml.tmp")), Is.False);
    }

    [Test]
    public void FailedReplacementRestoresTheOriginalFile()
    {
        var backing = new VirtualWritableDirProvider();
        var file = new ResPath("/actions.yml");
        InteractionPanelStorage.Write(backing, file, "original");
        var directory = new Mock<IWritableDirProvider>();
        directory.Setup(d => d.Exists(It.IsAny<ResPath>())).Returns((ResPath p) => backing.Exists(p));
        directory.Setup(d => d.Delete(It.IsAny<ResPath>())).Callback((ResPath p) => backing.Delete(p));
        directory.Setup(d => d.Open(It.IsAny<ResPath>(), It.IsAny<FileMode>(), It.IsAny<FileAccess>(), It.IsAny<FileShare>()))
            .Returns((ResPath p, FileMode m, FileAccess a, FileShare s) => backing.Open(p, m, a, s));
        directory.Setup(d => d.Rename(It.IsAny<ResPath>(), It.IsAny<ResPath>())).Callback((ResPath from, ResPath to) =>
        {
            if (from == new ResPath("/actions.yml.tmp")) throw new IOException("Simulated replacement failure");
            backing.Rename(from, to);
        });
        Assert.Throws<IOException>(() => InteractionPanelStorage.Write(directory.Object, file, "new"));
        Assert.That(backing.ReadAllText(file), Is.EqualTo("original"));
    }
}

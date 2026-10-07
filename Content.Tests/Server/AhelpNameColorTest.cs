using Content.Server.Administration.Systems;
using NUnit.Framework;
using Robust.Shared.Utility;

namespace Content.Tests.Server;

[TestFixture]
public sealed class AhelpNameColorTest
{
    [TestCase("#123456", false, true, true, "#123456")]
    [TestCase("#123456", true, false, true, "#123456")]
    [TestCase(null, true, false, true, "purple")]
    [TestCase(null, false, true, true, "red")]
    [TestCase(null, false, false, true, "#9370D8")]
    [TestCase(null, false, false, false, null)]
    public void CustomAdminColorOverridesDonorAndDefaultColors(string? custom, bool mentor, bool admin, bool supporter, string? expected)
        => Assert.That(BwoinkSystem.SelectAhelpNameColor(custom, mentor, admin, supporter), Is.EqualTo(expected));

    [Test]
    public void SenderNameIsEscapedAndColorDoesNotExtendToMessage()
    {
        const string name = "Player[color=red]";
        var sender = BwoinkSystem.FormatAhelpSender("", name, "#9370D8");
        Assert.That(sender, Is.EqualTo($"[color=#9370D8]{FormattedMessage.EscapeText(name)}[/color]"));
        Assert.That(sender + ": Message", Does.EndWith("[/color]: Message"));
    }
}

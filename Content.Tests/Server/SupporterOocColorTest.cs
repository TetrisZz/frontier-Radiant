using Content.Server.Chat.Managers;
using NUnit.Framework;

namespace Content.Tests.Server;

[TestFixture]
public sealed class SupporterOocColorTest
{
    [TestCase(false, true, null, "#9370D8")]
    [TestCase(false, true, "#aa00ff", "#9370D8")]
    [TestCase(true, true, "#aa00ff", null)]
    [TestCase(true, false, "#aa00ff", null)]
    [TestCase(false, false, "#aa00ff", "#aa00ff")]
    [TestCase(false, false, null, null)]
    public void SubscriptionColorsRespectPriorityAndRevocation(bool admin, bool supporter, string? patron, string? expected)
        => Assert.That(ChatManager.SelectOocNameColor(admin, supporter, patron), Is.EqualTo(expected));
}

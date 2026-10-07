using System.Linq;
using Content.Server.Discord.DiscordLink;
using NetCord;
using NetCord.Gateway;
using NUnit.Framework;

namespace Content.Tests.Server;

[TestFixture]
public sealed class DiscordPresenceTest
{
    [TestCase(0)]
    [TestCase(12)]
    public void ActivityDisplaysConnectedPlayers(int players)
    {
        var presence = DiscordLink.CreatePlayerPresence(players);
        Assert.That(presence.StatusType, Is.EqualTo(UserStatusType.Online));
        var activities = presence.Activities!.ToArray();
        Assert.That(activities, Has.Length.EqualTo(1));
        Assert.That(activities[0].Name, Is.EqualTo($"Онлайн сервера: {players}"));
        Assert.That(activities[0].Type, Is.EqualTo(UserActivityType.Watching));
    }

    [Test]
    public void DisabledOnlineClearsActivity()
        => Assert.That(DiscordLink.CreatePlayerPresence(null).Activities, Is.Empty);
}

using System.Linq;
using System.Net;
using Content.Server.Database;
using Content.Shared._radiant.Supporters;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Server.Player;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Preferences;

[TestFixture]
public sealed class SupporterAccessTest
{
    [Test]
    public async Task AdminGrantWorksWithoutDiscordAndResetsToThirtyDays()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true, Fresh = true, Destructive = true,
        });
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var host = server.ResolveDependency<IConsoleHost>();
        var session = server.ResolveDependency<IPlayerManager>().Sessions.Single();
        var offline = new NetUserId(Guid.NewGuid());
        const string offlineName = "OfflineSupporter";
        await db.UpdatePlayerRecordAsync(offline, offlineName, IPAddress.Loopback, null);

        var expiry = await Give(session.Name, session.UserId);
        Assert.That(await db.GetDiscordLinkAsync(session.UserId), Is.Null);
        var system = server.ResolveDependency<IEntityManager>().System<SharedSupporterSystem>();
        await server.WaitAssertion(() =>
        {
            Assert.That(server.ResolveDependency<IConfigurationManager>().GetCVar(SupporterCVars.Enabled), Is.False);
            Assert.That(system.HasAccess(session.UserId), Is.True);
            var loadout = server.ResolveDependency<IPrototypeManager>().Index<LoadoutPrototype>("HomUplinkRadio100");
            Assert.That(loadout.Effects[0].Validate(new HumanoidCharacterProfile(),
                new RoleLoadout("JobContractor"), session, IoCManager.Instance!, out _), Is.True);
        });
        await pair.Client.WaitAssertion(() =>
        {
            var clientSystem = pair.Client.ResolveDependency<IEntityManager>().System<SharedSupporterSystem>();
            Assert.That(clientSystem.HasAccess(session.UserId), Is.True);
        });

        var renewed = await Give(session.UserId.ToString(), session.UserId, expiry);
        Assert.That((await db.GetSupporterGrantsAsync(renewed)).ContainsKey(session.UserId), Is.False,
            "An expiry equal to the current time must not be treated as active.");
        await Give(offlineName, offline);
        await server.WaitAssertion(() => Assert.That(system.HasAccess(offline), Is.True));
        await pair.CleanReturnAsync();

        async Task<DateTime> Give(string target, NetUserId userId, DateTime previous = default)
        {
            var started = DateTime.UtcNow;
            await server.WaitAssertion(() => host.ExecuteCommand($"supportergrant \"{target}\""));
            while (DateTime.UtcNow - started < TimeSpan.FromSeconds(10))
            {
                await pair.RunTicksSync(4);
                if ((await db.GetSupporterGrantsAsync(DateTime.UtcNow)).TryGetValue(userId, out var stored) &&
                    stored > previous)
                {
                    Assert.That(stored, Is.InRange(started.AddDays(30), DateTime.UtcNow.AddDays(30)));
                    // Process the server-side grant update and its client event after the database write.
                    await pair.RunTicksSync(5);
                    return stored;
                }
            }

            throw new TimeoutException("The supporter grant was not saved.");
        }
    }

    [Test]
    public async Task RoleEventsPersistOfflineAccessAndRevocation()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var account = new NetUserId(Guid.NewGuid());
        const ulong discord = 123456789012345679;
        const ulong role = 123456789012345680;
        await db.TryLinkDiscordAsync(account, discord.ToString());
        Task update = Task.CompletedTask;
        await server.WaitAssertion(() =>
        {
            var config = server.ResolveDependency<IConfigurationManager>();
            config.SetCVar(SupporterCVars.DiscordRole, role.ToString());
            config.SetCVar(SupporterCVars.Enabled, true);
            update = server.ResolveDependency<IEntityManager>()
                .System<Content.Server._radiant.Supporters.SupporterSystem>().ApplyDiscordRoles(discord, [role]);
        });
        await PumpUpdate();
        Assert.That((await db.GetSupporterLinksAsync())[account].RoleId, Is.EqualTo(role.ToString()));
        await server.WaitAssertion(() =>
        {
            var system = server.ResolveDependency<IEntityManager>().System<Content.Server._radiant.Supporters.SupporterSystem>();
            Assert.That(system.HasAccess(account), Is.True, "Offline players must receive persisted access.");
            update = system.ApplyDiscordRoles(discord, []);
        });
        await PumpUpdate();
        Assert.That((await db.GetSupporterLinksAsync())[account].RoleId, Is.Empty);
        await server.WaitAssertion(() =>
        {
            Assert.That(server.ResolveDependency<IEntityManager>().System<SharedSupporterSystem>().HasAccess(account), Is.False);
            server.ResolveDependency<IConfigurationManager>().SetCVar(SupporterCVars.Enabled, false);
        });
        await pair.CleanReturnAsync();

        async Task PumpUpdate()
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);
            while (!update.IsCompleted && DateTime.UtcNow < deadline)
                await server.WaitRunTicks(1);
            Assert.That(update.IsCompleted, Is.True, "Role synchronization did not finish.");
            await update;
        }
    }

    [Test]
    public async Task AccountLinksAreUniqueAndUnverifiedSubscriptionsCannotUnlockLoadouts()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        var server = pair.Server;
        var db = server.ResolveDependency<IServerDbManager>();
        var first = new NetUserId(Guid.NewGuid());
        var second = new NetUserId(Guid.NewGuid());
        const string discord = "123456789012345678";
        Assert.That(await db.TryLinkDiscordAsync(first, discord), Is.True);
        Assert.That(await db.TryLinkDiscordAsync(first, discord), Is.True, "Retrying the same link should be idempotent.");
        Assert.That(await db.TryLinkDiscordAsync(second, discord), Is.False);
        Assert.That(await db.TryLinkDiscordAsync(first, "987654321098765432"), Is.False);
        Assert.That(await db.GetDiscordLinkAsync(first), Is.EqualTo(discord));
        await db.RemoveDiscordLinkAsync(first);
        Assert.That(await db.GetDiscordLinkAsync(first), Is.Null);
        Assert.That(await db.TryLinkDiscordAsync(second, discord), Is.True);

        await server.WaitAssertion(() =>
        {
            var config = server.ResolveDependency<IConfigurationManager>();
            var prototypes = server.ResolveDependency<IPrototypeManager>();
            var entities = server.ResolveDependency<IEntityManager>();
            var loadout = prototypes.Index<LoadoutPrototype>("HomUplinkRadio100");
            Assert.That(loadout.Effects, Has.Count.EqualTo(1));
            Assert.That(loadout.Effects[0], Is.TypeOf<SupporterLoadoutEffect>());
            config.SetCVar(SupporterCVars.Enabled, true);
            Assert.That(entities.System<SharedSupporterSystem>().HasAccess(second), Is.False,
                "A stored account link alone must not grant subscription privileges.");
            Assert.That(loadout.Effects[0].Validate(new HumanoidCharacterProfile(),
                new RoleLoadout("JobContractor"), null, IoCManager.Instance!, out _), Is.False);
            config.SetCVar(SupporterCVars.Enabled, false);
        });
        await pair.CleanReturnAsync();
    }
}

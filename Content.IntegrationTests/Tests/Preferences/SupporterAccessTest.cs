using Content.Server.Database;
using Content.Shared._radiant.Supporters;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Preferences;

[TestFixture]
public sealed class SupporterAccessTest
{
    [Test]
    public async Task RoleEventsPersistOfflineAccessAndRevocation()
    {
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
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
        var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
        using var server = instance;
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
    }
}

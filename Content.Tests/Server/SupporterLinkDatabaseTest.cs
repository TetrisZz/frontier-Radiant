using System;
using System.Threading.Tasks;
using Content.Server.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Content.Tests.Server;

[TestFixture]
public sealed class SupporterLinkDatabaseTest
{
    [Test]
    public void PostgresSnapshotIncludesAccountLinks()
    {
        // Model comparison does not connect to PostgreSQL.
        var options = new DbContextOptionsBuilder<PostgresServerDbContext>()
            .UseNpgsql("Host=localhost;Database=supporter_model_test").Options;
        using var db = new PostgresServerDbContext(options);
        Assert.That(db.Database.HasPendingModelChanges(), Is.False);
    }

    [Test]
    public async Task MigrationsPersistLinksAndRejectSharedAccounts()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteServerDbContext>().UseSqlite(connection).Options;
        var userId = Guid.NewGuid();
        const string discordId = "123456789012345678";
        await using (var db = new SqliteServerDbContext(options))
        {
            await db.Database.MigrateAsync();
            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
            db.RadiantDiscordLinks.Add(new RadiantDiscordLink { UserId = userId, DiscordUserId = discordId, SupporterRoleId = "123456789012345680" });
            await db.SaveChangesAsync();
        }

        // Reopening the context must preserve the link without character or nickname dependencies.
        await using (var db = new SqliteServerDbContext(options))
        {
            Assert.That((await db.RadiantDiscordLinks.SingleAsync()).UserId, Is.EqualTo(userId));
            Assert.That((await db.RadiantDiscordLinks.SingleAsync()).SupporterRoleId, Is.EqualTo("123456789012345680"));
            db.RadiantDiscordLinks.Add(new RadiantDiscordLink { UserId = Guid.NewGuid(), DiscordUserId = discordId });
            Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
        }

        await using (var db = new SqliteServerDbContext(options))
        {
            db.RadiantDiscordLinks.Add(new RadiantDiscordLink { UserId = userId, DiscordUserId = "987654321098765432" });
            Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
        }
    }
}

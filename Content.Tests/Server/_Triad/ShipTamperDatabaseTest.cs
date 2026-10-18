using System;
using System.Threading.Tasks;
using Content.Server.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Content.Tests.Server._Triad;

[TestFixture]
public sealed class ShipTamperDatabaseTest
{
    [Test]
    public void PostgresModelMatchesSnapshot()
    {
        var options = new DbContextOptionsBuilder<PostgresServerDbContext>()
            .UseNpgsql("Host=localhost;Database=ship_tamper_model_test").Options;
        using var db = new PostgresServerDbContext(options);
        Assert.That(db.Database.HasPendingModelChanges(), Is.False);
    }

    [Test]
    public async Task SqliteMigrationPersistsAuditAndPermits()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteServerDbContext>().UseSqlite(connection).Options;
        var player = Guid.NewGuid();
        await using (var db = new SqliteServerDbContext(options))
        {
            await db.Database.MigrateAsync();
            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
            db.TriadShipyardAuditEvents.Add(new TriadShipyardAuditEvent
            {
                At = DateTime.UtcNow, PlayerUserId = player, ShipName = "Test ship",
                EventType = TriadShipyardEventType.SaveSigned, ShipHash = new byte[] { 1, 2, 3 }
            });
            db.TriadShipyardMigrationPermits.Add(new TriadShipyardMigrationPermit
            {
                PlayerUserId = player, GrantedByAdminId = Guid.NewGuid(), GrantedAt = DateTime.UtcNow
            });
            await db.SaveChangesAsync();
        }
        await using (var db = new SqliteServerDbContext(options))
        {
            Assert.That((await db.TriadShipyardAuditEvents.SingleAsync()).ShipName, Is.EqualTo("Test ship"));
            Assert.That((await db.TriadShipyardMigrationPermits.SingleAsync()).PlayerUserId, Is.EqualTo(player));
            db.TriadShipyardMigrationPermits.Add(new TriadShipyardMigrationPermit
            {
                PlayerUserId = player, GrantedByAdminId = Guid.NewGuid(), GrantedAt = DateTime.UtcNow
            });
            Assert.ThrowsAsync<DbUpdateException>(async () => await db.SaveChangesAsync());
        }
    }
}

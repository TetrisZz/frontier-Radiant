using System;
using System.Threading.Tasks;
using Content.Server.Database;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using NUnit.Framework;

namespace Content.Tests.Server;

[TestFixture]
public sealed class GhostSkinDatabaseTest
{
    [Test]
    public async Task ExistingAccountsReceiveFreeDefaultWhenUpgraded()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteServerDbContext>().UseSqlite(connection).Options;
        await using var db = new SqliteServerDbContext(options);
        var migrator = db.GetService<IMigrator>();
        await migrator.MigrateAsync("20261011000000_CharacterCareer");
        await db.Database.ExecuteSqlInterpolatedAsync($"INSERT INTO preference (user_id, selected_character_slot, admin_ooc_color, construction_favorites) VALUES ({Guid.NewGuid().ToString()}, 0, {"#ffffff"}, {"[]"})");
        await db.Database.MigrateAsync();
        var preference = await db.Preference.SingleAsync();
        Assert.That(preference.GhostSkin, Is.EqualTo("GhostClassic"));
        Assert.That(preference.AdminOOCColor, Is.EqualTo("#ffffff"));
    }

    [Test]
    public void PostgresSnapshotIncludesGhostPreference()
    {
        var options = new DbContextOptionsBuilder<PostgresServerDbContext>()
            .UseNpgsql("Host=localhost;Database=ghost_skin_model_test").Options;
        using var db = new PostgresServerDbContext(options);
        Assert.That(db.Database.HasPendingModelChanges(), Is.False);
    }

    [Test]
    public async Task SqliteMigrationAndReconnectPreserveIndependentAccountChoices()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var options = new DbContextOptionsBuilder<SqliteServerDbContext>().UseSqlite(connection).Options;
        var first = Guid.NewGuid();
        var second = Guid.NewGuid();
        await using (var db = new SqliteServerDbContext(options))
        {
            await db.Database.MigrateAsync();
            Assert.That(db.Database.HasPendingModelChanges(), Is.False);
            db.Preference.Add(new Preference { UserId = first, AdminOOCColor = "#ffffff" });
            db.Preference.Add(new Preference { UserId = second, AdminOOCColor = "#ffffff" });
            await db.SaveChangesAsync();
        }
        await using (var db = new SqliteServerDbContext(options))
        {
            var prefs = await db.Preference.SingleAsync(p => p.UserId == first);
            Assert.That(prefs.GhostSkin, Is.EqualTo("GhostClassic"));
            prefs.GhostSkin = "GhostFoxMask";
            await db.SaveChangesAsync();
        }
        await using (var db = new SqliteServerDbContext(options))
        {
            var prefs = await db.Preference.SingleAsync(p => p.UserId == first);
            Assert.That(prefs.GhostSkin, Is.EqualTo("GhostFoxMask"));
            prefs.SelectedCharacterSlot = 3;
            prefs.AdminOOCColor = "#ff0000";
            await db.SaveChangesAsync();
        }
        await using (var db = new SqliteServerDbContext(options))
        {
            Assert.That((await db.Preference.SingleAsync(p => p.UserId == first)).GhostSkin, Is.EqualTo("GhostFoxMask"));
            Assert.That((await db.Preference.SingleAsync(p => p.UserId == second)).GhostSkin, Is.EqualTo("GhostClassic"));
        }
    }
}

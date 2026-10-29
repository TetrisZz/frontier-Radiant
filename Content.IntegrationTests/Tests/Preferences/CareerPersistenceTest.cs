using Content.Server.Database;
using Content.Shared._radiant.Careers;
using Content.Shared.Preferences;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Network;

namespace Content.IntegrationTests.Tests.Preferences;

[TestFixture]
public sealed class CareerPersistenceTest
{
    [Test]
    public void CloneDoesNotShareProgressOrPendingRewards()
    {
        var original = new CareerData
        {
            CharacterId = Guid.NewGuid(), Profession = "Mining", Level = 1,
            Progress = new() { ["duty"] = 42 }, PendingRewards = new() { 1 }, BoxId = Guid.NewGuid(),
            PatientHealing = new() { ["patient"] = 200 },
        };
        var copy = original.Clone();
        copy.Progress["duty"] = 100;
        copy.PendingRewards.Clear();
        copy.PatientHealing.Clear();
        Assert.That(original.Progress["duty"], Is.EqualTo(42));
        Assert.That(original.PendingRewards, Has.Count.EqualTo(1));
        Assert.That(original.PatientHealing["patient"], Is.EqualTo(200));
        Assert.That(copy.CharacterId, Is.EqualTo(original.CharacterId));
        Assert.That(copy.BoxId, Is.EqualTo(original.BoxId));
    }

    [Test]
    public async Task CareerWritesPreserveAppearanceBankAndReplacementIdentity()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Fresh = true, Destructive = true });
        using var connection = new SqliteConnection("Data Source=:memory:");
        connection.Open();
        var options = new DbContextOptionsBuilder<SqliteServerDbContext>().UseSqlite(connection).Options;
        var db = new ServerDbSqlite(() => options, true,
            pair.Server.ResolveDependency<IConfigurationManager>(), true,
            pair.Server.ResolveDependency<ILogManager>().GetSawmill("db.ops"));
        try
        {
            Assert.That(await db.HasPendingModelChanges(), Is.False);
            var user = new NetUserId(Guid.NewGuid());
            var initial = new HumanoidCharacterProfile().WithName("Career Original").WithBankBalance(12345);
            await db.InitPrefsAsync(user, initial);
            var loaded = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(user))!.Characters[0];
            var id = loaded.Career.CharacterId;
            Assert.That(id, Is.Not.EqualTo(Guid.Empty));
            var staleEditor = new HumanoidCharacterProfile(loaded);
            var progress = new CareerData
            {
                CharacterId = id, Profession = "Mining", Level = 2,
                Progress = new() { ["wildlife"] = 95 }, PendingRewards = new() { 1, 2 },
                StarterBoxGranted = true, BoxTier = 2, BoxId = Guid.NewGuid(),
                PatientHealing = new() { ["patient"] = 200 },
            };
            await db.SaveCharacterCareerAsync(user, 0, id, progress);
            await db.SaveCharacterSlotAsync(user, staleEditor.WithName("Career Renamed").WithBankBalance(777), 0);
            loaded = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(user))!.Characters[0];
            Assert.That(loaded.Name, Is.EqualTo("Career Renamed"));
            Assert.That(loaded.BankBalance, Is.EqualTo(777));
            Assert.That(loaded.Career.Profession, Is.EqualTo("Mining"));
            Assert.That(loaded.Career.Progress["wildlife"], Is.EqualTo(95));
            Assert.That(loaded.Career.BoxId, Is.EqualTo(progress.BoxId));
            Assert.That(loaded.Career.PatientHealing["patient"], Is.EqualTo(200));
            await db.SaveCharacterCareerAsync(user, 0, id, progress);
            loaded = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(user))!.Characters[0];
            Assert.That(loaded.BankBalance, Is.EqualTo(777), "Career-only writes cannot restore stale money");
            await db.SaveCharacterSlotAsync(user, initial.WithName("Replacement"), 0, replaceCharacter: true);
            loaded = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(user))!.Characters[0];
            Assert.That(loaded.Career.CharacterId, Is.Not.EqualTo(id));
            Assert.That(loaded.Career.Profession, Is.EqualTo("Adventurer"));
            Assert.That(loaded.Career.PendingRewards, Is.Empty);
            await db.SaveCharacterCareerAsync(user, 0, id, progress);
            loaded = (HumanoidCharacterProfile) (await db.GetPlayerPreferencesAsync(user))!.Characters[0];
            Assert.That(loaded.Career.Profession, Is.EqualTo("Adventurer"), "Old identity cannot overwrite replacement");
            var box = await db.PurchaseSafetyDepositBox(user.UserId, 0, "Replacement", "SafetyDepositBoxSmall");
            await db.DepositSafetyDepositBoxItems(box.BoxId, new() { "retained contents" });
            Assert.That(await db.UpgradeCareerSafetyDepositBox(box.BoxId, Guid.NewGuid(), 0,
                "SafetyDepositBoxSmall", "SafetyDepositBoxMedium"), Is.False);
            Assert.That(await db.UpgradeCareerSafetyDepositBox(box.BoxId, user.UserId, 0,
                "SafetyDepositBoxSmall", "SafetyDepositBoxMedium"), Is.True);
            var upgraded = await db.GetSafetyDepositBox(box.BoxId);
            Assert.That(upgraded!.BoxId, Is.EqualTo(box.BoxId));
            Assert.That(upgraded.ProtoId, Is.EqualTo("SafetyDepositBoxMedium"));
            Assert.That(upgraded.Items, Has.Count.EqualTo(1), "Upgrading cannot erase deposited items");
            TestContext.Out.WriteLine("Career persistence, partial writes and replacement identity assertions completed");
        }
        catch (Exception exception)
        {
            TestContext.Out.WriteLine($"Career persistence regression failure: {exception}");
            throw;
        }
        await pair.CleanReturnAsync();
    }
}

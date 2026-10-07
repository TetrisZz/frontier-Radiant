using System.Collections.Generic;
using System.Linq;
using Content.Server.Database;
using Content.Shared.GameTicking;
using Content.Shared.Humanoid;
using Content.Shared.Preferences;
using Content.Shared.Preferences.Loadouts;
using Content.Shared.Preferences.Loadouts.Effects;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Log;
using Robust.Shared.Maths;
using Robust.Shared.Network;
using Robust.UnitTesting;

namespace Content.IntegrationTests.Tests.Preferences
{
    [TestFixture]
    public sealed class ServerDbSqliteTests
    {
        [TestPrototypes]
        private const string Prototypes = @"
- type: dataset
  id: sqlite_test_names_first_male
  values:
  - Aaden

- type: dataset
  id: sqlite_test_names_first_female
  values:
  - Aaliyah

- type: dataset
  id: sqlite_test_names_last
  values:
  - Ackerley";

        private static HumanoidCharacterProfile CharlieCharlieson()
        {
            return new HumanoidCharacterProfile() // Frontier - added HumanoidCharacterProfile
            {
                Name = "Charlie Charlieson",
                FlavorText = "The biggest boy around.",
                Species = "Human",
                Height = 1,
                Width = 1,
				Voice = "Arc_warden_dota_2", // Corvax-TTS
                Age = 21,
                Appearance = new(
                    "Afro",
                    Color.Aqua,
                    "Shaved",
                    Color.Aquamarine,
                    Color.Azure,
                    Color.Beige,
                    new ())
            }.WithBankBalance(27000); // Frontier - accessor issue
        }

        private static ServerDbSqlite GetDb(RobustIntegrationTest.ServerIntegrationInstance server)
        {
            var cfg = server.ResolveDependency<IConfigurationManager>();
            var opsLog = server.ResolveDependency<ILogManager>().GetSawmill("db.ops");
            var builder = new DbContextOptionsBuilder<SqliteServerDbContext>();
            var conn = new SqliteConnection("Data Source=:memory:");
            conn.Open();
            builder.UseSqlite(conn);
            return new ServerDbSqlite(() => builder.Options, true, cfg, true, opsLog);
        }

        [Test]
        public async Task TestUserDoesNotExist()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            // Database should be empty so a new GUID should do it.
            Assert.That(await db.GetPlayerPreferencesAsync(NewUserId()), Is.Null);

            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestInitPrefs()
        {
            var pair = await PoolManager.GetServerClient();
            var db = GetDb(pair.Server);
            var username = new NetUserId(new Guid("640bd619-fc8d-4fe2-bf3c-4a5fb17d6ddd"));
            const int slot = 0;
            var originalProfile = CharlieCharlieson();
            await db.InitPrefsAsync(username, originalProfile);
            var prefs = await db.GetPlayerPreferencesAsync(username);
            Assert.That(prefs.Characters.Single(p => p.Key == slot).Value.MemberwiseEquals(originalProfile));
            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestProfessionalSkillPersistence()
        {
            var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
            using var server = instance;
            var db = GetDb(server);
            Assert.That(await db.HasPendingModelChanges(), Is.False);
            var user = NewUserId();
            var profile = CharlieCharlieson()
                .WithSkillLevel(Content.Shared._radiant.Skills.ProfessionalSkill.Medicine, 4)
                .WithSkillLevel(Content.Shared._radiant.Skills.ProfessionalSkill.Piloting, 3);
            await db.InitPrefsAsync(user, profile);
            var prefs = await db.GetPlayerPreferencesAsync(user);
            Assert.That(((HumanoidCharacterProfile) prefs!.Characters[0]).SkillLevels, Is.EqualTo(profile.SkillLevels));
            profile = profile.WithSkillLevel(Content.Shared._radiant.Skills.ProfessionalSkill.Piloting, 1);
            await db.SaveCharacterSlotAsync(user, profile, 0);
            prefs = await db.GetPlayerPreferencesAsync(user);
            Assert.That(((HumanoidCharacterProfile) prefs!.Characters[0]).SkillLevels, Is.EqualTo(profile.SkillLevels));
            await server.WaitAssertion(() =>
            {
                var entities = server.ResolveDependency<Robust.Shared.GameObjects.IEntityManager>();
                var system = entities.System<Content.Shared._radiant.Skills.SharedProfessionalSkillsSystem>();
                var userEntity = entities.SpawnEntity(null, Robust.Shared.Map.MapCoordinates.Nullspace);
                var welder = entities.SpawnEntity("PowerDrill", Robust.Shared.Map.MapCoordinates.Nullspace);
                Assert.That(system.CanUse(userEntity, welder, Content.Shared._radiant.Skills.SkillAction.Use, false), Is.True);
                var skills = entities.AddComponent<Content.Shared._radiant.Skills.ProfessionalSkillsComponent>(userEntity);
                Assert.That(system.CanUse(userEntity, welder, Content.Shared._radiant.Skills.SkillAction.Use, false), Is.False);
                skills.Levels[(int) Content.Shared._radiant.Skills.ProfessionalSkill.Engineering] = 1;
                Assert.That(system.CanUse(userEntity, welder, Content.Shared._radiant.Skills.SkillAction.Use, false), Is.True);
                entities.DeleteEntity(welder);
                entities.DeleteEntity(userEntity);
            });
        }

        [Test]
        public async Task TestDossierPersistence()
        {
            var (instance, _) = await PoolManager.GenerateServer(new PoolSettings(), TestContext.Out);
            using var server = instance;
            var db = GetDb(server);
            var user = NewUserId();
            var profile = CharlieCharlieson().WithPersonalDetails("Prospekt, 13", "Married", "One child",
                "NT-5755", "Scar above left brow", "", "", "", "");
            await db.InitPrefsAsync(user, profile);
            await db.SaveCharacterDossierAsync(user, 0, "{\"MedicalNotes\":\"Allergy\"}");

            // Updating the player-controlled profile must not overwrite staff records.
            await db.SaveCharacterSlotAsync(user, profile.WithPersonalDetails("Prospekt, 14", "Married", "One child",
                "NT-5755", "Scar above left brow", "", "", "", ""), 0);
            var reloaded = await db.GetPlayerPreferencesAsync(user);
            Assert.That(((HumanoidCharacterProfile) reloaded!.Characters[0]).Residence, Is.EqualTo("Prospekt, 14"));
            Assert.That(await db.GetCharacterDossierAsync(user, 0), Does.Contain("Allergy"));

            // A randomized replacement in the same slot must not inherit the former identity.
            var replacement = CharlieCharlieson().WithName("Aileen Abbott").WithSpecies("SlimePerson")
                .WithPersonalDetails("New address", "Single", "",
                "", "", "", "", "", "").WithCitizenship(Content.Shared._radiant.Passports.RadiantCitizenship.NT);
            await db.SaveCharacterSlotAsync(user, replacement, 0, replaceCharacter: true);
            reloaded = await db.GetPlayerPreferencesAsync(user);
            var replaced = (HumanoidCharacterProfile) reloaded!.Characters[0];
            Assert.That(replaced.Name, Is.EqualTo("Aileen Abbott"));
            Assert.That(replaced.Species.Id, Is.EqualTo("SlimePerson"));
            Assert.That(replaced.Citizenship, Is.EqualTo(replacement.Citizenship));
            Assert.That(replaced.Residence, Is.EqualTo("New address"));
            Assert.That(await db.GetCharacterDossierAsync(user, 0), Is.EqualTo("{}"));
        }

        [Test]
        public async Task TestDeleteCharacter()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            var username = new NetUserId(new Guid("640bd619-fc8d-4fe2-bf3c-4a5fb17d6ddd"));
            await db.InitPrefsAsync(username, new HumanoidCharacterProfile());
            await db.SaveCharacterSlotAsync(username, CharlieCharlieson(), 1);
            var selected = await db.GetPlayerPreferencesAsync(username);
            Assert.That(selected!.SelectedCharacterIndex, Is.EqualTo(1),
                "Saving a new character must select it for the next spawn and reconnect.");
            await db.SaveSelectedCharacterIndexAsync(username, 1);
            await db.SaveCharacterSlotAsync(username, null, 1);
            var prefs = await db.GetPlayerPreferencesAsync(username);
            Assert.That(!prefs.Characters.Any(p => p.Key != 0));
            await pair.CleanReturnAsync();
        }

        [Test]
        public async Task TestNoPendingDatabaseChanges()
        {
            var pair = await PoolManager.GetServerClient();
            var server = pair.Server;
            var db = GetDb(server);
            Assert.That(async () => await db.HasPendingModelChanges(), Is.False,
                "The database has pending model changes. Add a new migration to apply them. See https://learn.microsoft.com/en-us/ef/core/managing-schemas/migrations");
            await pair.CleanReturnAsync();
        }

        private static NetUserId NewUserId()
        {
            return new(Guid.NewGuid());
        }
    }
}

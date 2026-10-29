using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Client.Lobby;
using Content.Client.Players.PlayTimeTracking;
using Content.Shared._radiant.Careers;
using Content.Shared._radiant.Skills;
using Content.Shared.Preferences;
using Content.Server.Preferences.Managers;
using Content.Server.Database;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using ClientCareers = Content.Client._radiant.Careers.CareerSystem;

namespace Content.IntegrationTests.Tests.Preferences;

public sealed class CareerSelectionTest
{
    [Test]
    public async Task ProfessionChangesReachBothPreferenceCachesWithoutReconnecting()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { InLobby = true, Fresh = true, Destructive = true });
        try
        {
            Task update = Task.CompletedTask;
            Guid serviceBoxId = default;
            await pair.Server.WaitPost(() =>
            {
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                var manager = pair.Server.ResolveDependency<IServerPreferencesManager>();
                var prefs = manager.GetPreferences(session.UserId);
                var profile = ((HumanoidCharacterProfile)prefs.SelectedCharacter)
                    .WithSkillLevel(ProfessionalSkill.Salvage, 2)
                    .WithSkillLevel(ProfessionalSkill.Medicine, 1)
                    .WithSkillLevel(ProfessionalSkill.Engineering, 1);
                update = PrepareServiceStorage();
                async Task PrepareServiceStorage()
                {
                    await manager.SetProfile(session.UserId, prefs.SelectedCharacterIndex, profile);
                    profile = (HumanoidCharacterProfile)manager.GetPreferences(session.UserId).SelectedCharacter;
                    var db = pair.Server.ResolveDependency<IServerDbManager>();
                    var box = await db.PurchaseSafetyDepositBox(session.UserId.UserId, prefs.SelectedCharacterIndex,
                        profile.Name, "SafetyDepositBoxLarge");
                    serviceBoxId = box.BoxId;
                    profile.Career.StarterBoxGranted = true;
                    profile.Career.BoxTier = 3;
                    profile.Career.BoxId = serviceBoxId;
                    await db.SaveCharacterCareerAsync(session.UserId, prefs.SelectedCharacterIndex,
                        profile.Career.CharacterId, profile.Career);
                }
            });
            await update;
            await pair.Server.WaitPost(() =>
            {
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                update = pair.Server.ResolveDependency<IServerPreferencesManager>().RefreshPreferencesAsync(session, default);
            });
            await update;
            await pair.RunTicksSync(5);

            var refreshed = 0;
            await pair.Client.WaitPost(() =>
                pair.Client.ResolveDependency<JobRequirementsManager>().Updated += () => refreshed++);
            foreach (var (profession, job) in new[]
                     { ("Mining", "SalvageSpecialist"), ("Engineering", "StationEngineer"), ("Medical", "MedicalDoctor"), ("Adventurer", "Contractor"), ("Mining", "SalvageSpecialist") })
            {
                var previousUpdates = refreshed;
                await pair.Client.WaitPost(() => pair.Client.EntMan.System<ClientCareers>().SelectProfession(profession, true));
                var synchronized = false;
                for (var attempt = 0; attempt < 150; attempt++)
                {
                    await pair.RunTicksSync(3);
                    await pair.Client.WaitPost(() => synchronized =
                        pair.Client.ResolveDependency<IClientPreferencesManager>().Preferences?.SelectedCharacter
                            is HumanoidCharacterProfile profile && profile.Career.Profession == profession
                            && profile.JobPriorities.TryGetValue(job, out var priority) && priority == JobPriority.High);
                    if (synchronized) break;
                }
                Assert.That(synchronized, Is.True, profession);
                await pair.Server.WaitAssertion(() =>
                {
                    var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                    var profile = (HumanoidCharacterProfile)pair.Server.ResolveDependency<IServerPreferencesManager>()
                        .GetPreferences(session.UserId).SelectedCharacter;
                    Assert.That(profile.Career.Profession, Is.EqualTo(profession));
                    Assert.That(profile.Career.BoxId, Is.EqualTo(serviceBoxId));
                    Assert.That(profile.Career.BoxTier, Is.EqualTo(3));
                    Assert.That(profile.Career.StarterBoxGranted, Is.True,
                        "Changing profession must neither remove the service slot nor allow claiming another one.");
                    Assert.That(profile.JobPriorities[job], Is.EqualTo(JobPriority.High));
                    Assert.That(profile.JobPriorities.Where(entry => entry.Value != JobPriority.Never)
                        .All(entry => CareerJobRules.IsAllowed(pair.Server.ProtoMan, profession, entry.Key)), Is.True);
                });
                Assert.That(refreshed, Is.GreaterThan(previousUpdates), "An open role picker must receive the new requirements immediately.");
            }
            Guid owner = default;
            var slot = 0;
            await pair.Server.WaitPost(() =>
            {
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                owner = session.UserId.UserId;
                slot = pair.Server.ResolveDependency<IServerPreferencesManager>().GetPreferences(session.UserId).SelectedCharacterIndex;
            });
            Assert.That(await pair.Server.ResolveDependency<IServerDbManager>().GetPlayerSafetyDepositBoxes(owner, slot),
                Has.Count.EqualTo(1), "All profession changes must retain the same database slot.");
        }
        finally { await pair.CleanReturnAsync(); }
    }
}

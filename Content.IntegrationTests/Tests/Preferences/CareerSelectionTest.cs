using System;
using System.Linq;
using System.Threading.Tasks;
using Content.Client.Lobby;
using Content.Client.Players.PlayTimeTracking;
using Content.Shared._radiant.Careers;
using Content.Shared._radiant.Skills;
using Content.Shared.Preferences;
using Content.Server.Preferences.Managers;
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
            await pair.Server.WaitPost(() =>
            {
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                var manager = pair.Server.ResolveDependency<IServerPreferencesManager>();
                var prefs = manager.GetPreferences(session.UserId);
                var profile = ((HumanoidCharacterProfile)prefs.SelectedCharacter)
                    .WithSkillLevel(ProfessionalSkill.Salvage, 2)
                    .WithSkillLevel(ProfessionalSkill.Medicine, 1)
                    .WithSkillLevel(ProfessionalSkill.Engineering, 1);
                update = manager.SetProfile(session.UserId, prefs.SelectedCharacterIndex, profile);
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
                    Assert.That(profile.JobPriorities[job], Is.EqualTo(JobPriority.High));
                    Assert.That(profile.JobPriorities.Where(entry => entry.Value != JobPriority.Never)
                        .All(entry => CareerJobRules.IsAllowed(pair.Server.ProtoMan, profession, entry.Key)), Is.True);
                });
                Assert.That(refreshed, Is.GreaterThan(previousUpdates), "An open role picker must receive the new requirements immediately.");
            }
        }
        finally { await pair.CleanReturnAsync(); }
    }
}

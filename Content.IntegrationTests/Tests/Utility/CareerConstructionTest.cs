using System.Linq;
using Content.Server._radiant.Careers;
using Content.Server.Construction;
using Content.Server.Preferences.Managers;
using Content.Shared._radiant.Careers;
using Content.Shared.Preferences;
using Robust.Server.Player;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class CareerConstructionTest
{
    [Test]
    public async Task FinishedConstructionCountsOnceAndFramesRetainCredit()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true });
        var map = await pair.CreateTestMap();
        try
        {
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.ResolveDependency<IEntityManager>();
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                var prefs = pair.Server.ResolveDependency<IServerPreferencesManager>().GetPreferences(session.UserId);
                var profile = (HumanoidCharacterProfile) prefs.SelectedCharacter;
                var original = profile.Career;
                profile.Career = new CareerData { CharacterId = original.CharacterId, Profession = "Engineering", Level = 2 };
                try
                {
                    var engineer = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                    var actor = em.AddComponent<CareerActorComponent>(engineer);
                    actor.User = session.UserId;
                    actor.Slot = prefs.SelectedCharacterIndex;
                    actor.CharacterId = profile.Career.CharacterId;
                    actor.Job = "StationEngineer";

                    var girder = em.SpawnEntity("Girder", map.GridCoords);
                    Assert.That(em.System<ConstructionSystem>().ChangeNode(girder, engineer, "wall"), Is.True);
                    Assert.That(profile.Career.Progress["walls"], Is.EqualTo(1));

                    var window = em.SpawnEntity("Window", map.GridCoords);
                    var completed = new CareerConstructionCompletedEvent(window, engineer);
                    em.EventBus.RaiseEvent(EventSource.Local, ref completed);
                    em.EventBus.RaiseEvent(EventSource.Local, ref completed);
                    Assert.That(profile.Career.Progress["windows"], Is.EqualTo(1));

                    var frame = em.SpawnEntity("MachineFrame", map.GridCoords);
                    var machine = em.SpawnEntity("SMESBasicEmpty", map.GridCoords);
                    completed = new CareerConstructionCompletedEvent(machine, engineer, frame);
                    em.EventBus.RaiseEvent(EventSource.Local, ref completed);
                    Assert.That(profile.Career.Progress["smes"], Is.EqualTo(1));
                    var nextFrame = em.SpawnEntity("MachineFrame", map.GridCoords);
                    completed = new CareerConstructionCompletedEvent(nextFrame, engineer, machine);
                    em.EventBus.RaiseEvent(EventSource.Local, ref completed);
                    var rebuilt = em.SpawnEntity("SMESBasicEmpty", map.GridCoords);
                    completed = new CareerConstructionCompletedEvent(rebuilt, engineer, nextFrame);
                    em.EventBus.RaiseEvent(EventSource.Local, ref completed);
                    Assert.That(profile.Career.Progress["smes"], Is.EqualTo(1), "Reassembling the same frame is not new construction");

                    var mapped = em.SpawnEntity("SubstationBasic", map.GridCoords);
                    var mappedFrame = em.SpawnEntity("MachineFrame", map.GridCoords);
                    completed = new CareerConstructionCompletedEvent(mappedFrame, engineer, mapped);
                    em.EventBus.RaiseEvent(EventSource.Local, ref completed);
                    var reassembled = em.SpawnEntity("SubstationBasicEmpty", map.GridCoords);
                    completed = new CareerConstructionCompletedEvent(reassembled, engineer, mappedFrame);
                    em.EventBus.RaiseEvent(EventSource.Local, ref completed);
                    Assert.That(profile.Career.Progress.ContainsKey("substation"), Is.False);

                    var newSubstation = em.SpawnEntity("SubstationBasicEmpty", map.GridCoords);
                    completed = new CareerConstructionCompletedEvent(newSubstation, engineer);
                    em.EventBus.RaiseEvent(EventSource.Local, ref completed);
                    Assert.That(profile.Career.Progress["substation"], Is.EqualTo(1));
                    Assert.That(profile.Career.Progress.ContainsKey("duty"), Is.False);
                }
                finally
                {
                    profile.Career = original;
                }
            });
        }
        finally
        {
            await pair.Server.WaitPost(() => pair.Server.EntMan.DeleteEntity(map.MapUid));
            await pair.CleanReturnAsync();
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Numerics;
using Content.Client._NF.LateJoin.ListItems;
using Content.Client._NF.LateJoin.Windows;
using Content.Client._radiant.Passports;
using Content.Client.GameTicking.Managers;
using Content.Client.Lobby;
using Content.Shared._EE.Contractors.Components;
using Content.Shared._radiant.Passports;
using Content.Shared._radiant.Skills;
using Content.Shared.GameTicking;
using Content.Shared.Interaction.Events;
using Content.Shared.Preferences;
using Content.Shared.Roles;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Input;
using Robust.Shared.Map;
using Robust.Shared.Maths;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class PassportLobbyRefreshTest
{
    [Test]
    public async Task PassportFirstOpeningRendersItsCachedState()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            NetEntity passportNet = default;
            await pair.Server.WaitAssertion(() =>
            {
                var em = pair.Server.EntMan;
                var players = pair.Server.ResolveDependency<IPlayerManager>();
                var user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                players.SetAttachedEntity(players.Sessions.Single(), user);
                var passport = em.SpawnEntity("EEPassportZon", map.GridCoords);
                var data = em.GetComponent<PassportComponent>(passport);
                data.OwnerName = "First Opening Test";
                data.Species = "Human";
                data.Number = "FIRST-123";
                passportNet = em.GetNetEntity(passport);
                var use = new UseInHandEvent(user);
                em.EventBus.RaiseLocalEvent(passport, use);
            });
            await pair.RunTicksSync(30);
            await pair.Client.WaitAssertion(() =>
            {
                var em = pair.Client.EntMan;
                var passport = em.GetEntity(passportNet);
                Assert.That(em.System<SharedUserInterfaceSystem>().TryGetUiState<PassportUiState>(passport, PassportUiKey.Key, out var state), Is.True);
                Assert.That(state!.OwnerName, Is.EqualTo("First Opening Test"));
                var root = pair.Client.ResolveDependency<IUserInterfaceManager>().RootControl;
                var window = Descendants(root).OfType<PassportWindow>().Single();
                Assert.That(Descendants(window).Any(control => control.Name == "PassportCover"), Is.True,
                    "The first opening must show document pages rather than only the close button.");
            });
        }
        finally { await pair.CleanReturnAsync(); }
    }

    [Test]
    public async Task SwitchingCharacterSlotsRefreshesTheOpenLobbyRolePickerImmediately()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { InLobby = true, Fresh = true, Destructive = true });
        try
        {
            await pair.Client.WaitAssertion(() =>
            {
                var prefs = pair.Client.ResolveDependency<IClientPreferencesManager>();
                var original = (HumanoidCharacterProfile)prefs.Preferences!.SelectedCharacter;
                HumanoidCharacterProfile Profile(string profession)
                {
                    var profile = original.Clone().WithSkillLevel(ProfessionalSkill.Salvage, 2)
                        .WithSkillLevel(ProfessionalSkill.Medicine, 1).WithSkillLevel(ProfessionalSkill.Engineering, 1);
                    profile.Career.Profession = profession;
                    return profile;
                }
                prefs.UpdateCharacter(Profile("Mining"), 0);
                prefs.UpdateCharacter(Profile("Medical"), 1);
                var ticker = pair.Client.EntMan.System<ClientGameTicker>();
                var jobs = (IDictionary<NetEntity, StationJobInformation>)ticker.StationJobInformationList;
                jobs.Clear();
                jobs.Add(new NetEntity(123), new StationJobInformation("Profession role test", new Dictionary<ProtoId<JobPrototype>, int?>
                {
                    ["SalvageSpecialistInterview"] = 1, ["MedicalDoctorInterview"] = 1, ["StationEngineerInterview"] = 1,
                }, true, null, null));
                using var window = new PickerWindow();
                window.OpenCentered();
                Layout();
                Click(Descendants(window).OfType<BaseButton>().Single(button => button.Name == "StationTabButton"));
                Layout();
                var station = Descendants(window).OfType<StationListItem>().Single();
                Click(Descendants(station).OfType<BaseButton>().Single());
                AssertRole("MedicalDoctorInterview");
                prefs.SelectCharacter(0);
                AssertRole("SalvageSpecialistInterview");
                prefs.SelectCharacter(1);
                AssertRole("MedicalDoctorInterview");
                prefs.UpdateCharacter(Profile("Engineering"), 1);
                AssertRole("StationEngineerInterview");
                prefs.SelectCharacter(0);
                AssertRole("SalvageSpecialistInterview");
                // These assertions run within one client callback, before a server reply or reconnection.
                void Layout()
                {
                    var size = new Vector2(800, 600);
                    window.Measure(size);
                    window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                }
                void AssertRole(string role)
                {
                    var items = Descendants(window).OfType<JobListItem>().ToArray();
                    Assert.That(items, Has.Length.EqualTo(1));
                    var name = pair.Client.ProtoMan.Index<JobPrototype>(role).LocalizedName;
                    Assert.That(Descendants(items[0]).OfType<Label>().Single(label => label.Name == "JobText").Text,
                        Does.Contain(name), role);
                }
            });
            await pair.RunTicksSync(15);
        }
        finally { await pair.CleanReturnAsync(); }
    }

    private static IEnumerable<Control> Descendants(Control parent)
    {
        foreach (var child in parent.Children)
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }

    private static void Click(BaseButton button)
    {
        button.MuteSounds = true;
        var pointer = new ScreenCoordinates(button.GlobalPixelPosition + button.PixelSize / 2, button.Window?.Id ?? default);
        foreach (var (method, state) in new[] { ("KeyBindDown", BoundKeyState.Down), ("KeyBindUp", BoundKeyState.Up) })
        {
            var args = new GUIBoundKeyEventArgs(EngineKeyFunctions.UIClick, state, pointer, default, button.Size / 2, button.PixelSize / 2);
            typeof(BaseButton).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(button, new object[] { args });
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Content.Client._WF.SafetyDepositBox;
using Content.Client.Stylesheets;
using Content.Server._radiant.Careers;
using Content.Server.Power.Components;
using Content.Server.Preferences.Managers;
using Content.Shared._radiant.Careers;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.SafetyDepositBox.BUI;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Preferences;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Client.UserInterface.Controls;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Maths;

namespace Content.IntegrationTests.Tests.Utility;

public sealed class CareerTerminalTest
{
    [Test]
    public async Task CareerBoxesUpgradeOnlyAtLevelsTwoAndThreeThroughTheTerminal()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
            { Connected = true, Fresh = true, Destructive = true });
        try
        {
            var map = await pair.CreateTestMap();
            var em = pair.Server.EntMan;
            EntityUid user = default;
            EntityUid terminal = default;
            CareerData career = default!;
            CareerSafeState? state = null;
            await pair.Server.WaitAssertion(() =>
            {
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                var prefs = pair.Server.ResolveDependency<IServerPreferencesManager>().GetPreferences(session.UserId);
                var profile = (HumanoidCharacterProfile)prefs.SelectedCharacter;
                var character = profile.Career.CharacterId;
                user = em.SpawnEntity("MobHumanDummy", map.GridCoords);
                pair.Server.ResolveDependency<IPlayerManager>().SetAttachedEntity(session, user);
                var actor = em.AddComponent<CareerActorComponent>(user);
                actor.User = session.UserId;
                actor.Slot = prefs.SelectedCharacterIndex;
                actor.CharacterId = character;
                terminal = em.SpawnEntity("SafetyDepositConsole", map.GridCoords.Offset(new Vector2(1, 0)));
                var component = em.GetComponent<SafetyDepositConsoleComponent>(terminal);
                var boxUid = em.SpawnEntity("SafetyDepositBoxSmall", map.GridCoords);
                var box = em.GetComponent<SafetyDepositBoxComponent>(boxUid);
                box.BoxId = Guid.NewGuid();
                box.OwnerId = session.UserId.UserId;
                box.CharacterIndex = actor.Slot;
                Assert.That(em.System<ItemSlotsSystem>().TryInsert(terminal, component.BoxSlot, boxUid, user), Is.True);
                var rewards = em.System<CareerRewardSystem>();
                foreach (var (profession, job) in new[] { ("Mining", "SalvageSpecialist"), ("Medical", "MedicalDoctor"), ("Engineering", "StationEngineer") })
                {
                    actor.Job = job;
                    career = profile.Career = new CareerData
                    {
                        CharacterId = character, Profession = profession, StarterBoxGranted = true,
                        BoxTier = 1, BoxId = box.BoxId,
                    };
                    for (var level = 0; level <= pair.Server.ProtoMan.Index<CareerProfessionPrototype>(profession).Levels.Count; level++)
                    {
                        career.Level = level;
                        state = rewards.GetSafeState(terminal, user);
                        Assert.That(state, Is.Not.Null);
                        Assert.That(state!.UnlockedTier, Is.EqualTo(level >= 3 ? 3 : level >= 2 ? 2 : 1), $"{profession}, {level}");
                        Assert.That(state.CanUpgradeBox, Is.EqualTo(level >= 2), $"{profession}, {level}");
                    }
                    box.OwnerId = Guid.NewGuid();
                    Assert.That(rewards.GetSafeState(terminal, user)!.CanUpgradeBox, Is.False, "Another character's box cannot be upgraded.");
                    box.OwnerId = session.UserId.UserId;
                    var paper = em.SpawnEntity("Paper", map.GridCoords);
                    Assert.That(em.System<SharedStorageSystem>().Insert(boxUid, paper, out _, playSound: false), Is.True);
                    Assert.That(rewards.GetSafeState(terminal, user)!.CanUpgradeBox, Is.False, "Empty the box before replacing it.");
                    em.DeleteEntity(paper);
                }
                em.DeleteEntity(boxUid);
                actor.Job = "SalvageSpecialist";
                career = profile.Career = new CareerData { CharacterId = character, Profession = "Mining" };
                Assert.That(rewards.GetSafeState(terminal, user)!.CanClaimStarterBox, Is.True);
                Send(CareerSafeAction.ClaimStarterBox);
                Send(CareerSafeAction.ClaimStarterBox);
            });
            await WaitFor(() => career.StarterBoxGranted && em.System<CareerRewardSystem>().GetSafeState(terminal, user)?.Busy == false);
            for (var level = 2; level <= 3; level++)
            {
                await pair.Server.WaitAssertion(() =>
                {
                    career.Level = level;
                    var boxUid = em.EntityQuery<SafetyDepositBoxComponent>().Single(b => b.BoxId == career.BoxId).Owner;
                    var component = em.GetComponent<SafetyDepositConsoleComponent>(terminal);
                    Assert.That(em.System<ItemSlotsSystem>().TryInsert(terminal, component.BoxSlot, boxUid, user), Is.True);
                    Send(CareerSafeAction.UpgradeBox);
                });
                await WaitFor(() => career.BoxTier == level && em.System<CareerRewardSystem>().GetSafeState(terminal, user)?.Busy == false);
                await pair.RunTicksSync(1); // Replaced boxes are deleted at the end of the tick.
                await pair.Server.WaitAssertion(() =>
                {
                    var box = em.EntityQuery<SafetyDepositBoxComponent>().Single(b => b.BoxId == career.BoxId);
                    Assert.That(em.GetComponent<MetaDataComponent>(box.Owner).EntityPrototype!.ID,
                        Is.EqualTo(level == 2 ? "SafetyDepositBoxMedium" : "SafetyDepositBoxLarge"));
                    state = em.System<CareerRewardSystem>().GetSafeState(terminal, user);
                });
            }

            await pair.Server.WaitAssertion(() =>
            {
                var session = pair.Server.ResolveDependency<IPlayerManager>().Sessions.Single();
                var profile = (HumanoidCharacterProfile)pair.Server.ResolveDependency<IServerPreferencesManager>()
                    .GetPreferences(session.UserId).SelectedCharacter;
                var previousBalance = profile.BankBalance;
                em.EnsureComponent<BankAccountComponent>(user);
                career.PendingRewards.Add(1);
                Assert.That(em.System<CareerRewardSystem>().GetSafeState(terminal, user)!.CanClaimRewards, Is.True);
                Send(CareerSafeAction.ClaimRewards);
                Send(CareerSafeAction.ClaimRewards);
                Assert.That(em.System<CareerSystem>().TryGetCareer(user, out var current), Is.True);
                Assert.That(current.PendingRewards, Is.Empty);
                Assert.That(em.GetComponent<BankAccountComponent>(user).Balance,
                    Is.EqualTo(previousBalance + pair.Server.ProtoMan.Index<CareerLevelPrototype>("MiningLevel1").RewardMoney));
                state = em.System<CareerRewardSystem>().GetSafeState(terminal, user);
            });

            await pair.Client.WaitAssertion(() =>
            {
                using var window = new SafetyDepositConsoleWindow
                    { Stylesheet = new StyleNano(pair.Client.ResolveDependency<IResourceCache>()).Stylesheet };
                var ui = new SafetyDepositConsoleState(new(), 0, false, null, 1, 2, 3, 1) { Career = state };
                window.UpdateState(ui);
                Assert.That(Descendants(window).Single(c => c.Name == "CareerSection").Visible, Is.True);
                Assert.That(Descendants(window).OfType<Button>().Single(b => b.Name == "CareerUpgradeButton").Disabled, Is.True);
                var size = new Vector2(700, 760);
                window.SetSize = size;
                window.Measure(size);
                window.Arrange(UIBox2.FromDimensions(Vector2.Zero, size));
                foreach (var button in Descendants(window).OfType<Button>().Where(b => b.Name.StartsWith("Career") && b.Visible))
                    Assert.That(button.GlobalPosition.X + button.Size.X, Is.LessThanOrEqualTo(window.GlobalPosition.X + size.X));
                ui.Career = null;
                window.UpdateState(ui);
                Assert.That(Descendants(window).Single(c => c.Name == "CareerSection").Visible, Is.False);
            });

            void Send(CareerSafeAction action)
                => em.EventBus.RaiseLocalEvent(terminal, new CareerSafeActionMessage(action) { Actor = user });

            async Task WaitFor(Func<bool> condition)
            {
                for (var i = 0; i < 100; i++)
                {
                    var ready = false;
                    await pair.Server.WaitPost(() => ready = condition());
                    if (ready) return;
                    await pair.RunTicksSync(3);
                }
                Assert.Fail("Timed out waiting for the career box DB operation.");
            }
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
}

using Content.Server.GameTicking;
using Content.Shared._radiant.Skills;
using Content.Shared.GameTicking;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Preferences;

public sealed class SkillSelectionGateTest
{
    [Test]
    public async Task EmptySkillsCannotReadyOrJoinAndOnePointIsEnough()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Fresh = true, Destructive = true, InLobby = true,
        });
        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            var player = pair.Player!;
            Assert.That(ticker.GetPlayerProfile(player).SkillPointsSpent, Is.Zero);
            ticker.ToggleReady(player, true);
            Assert.That(ticker.PlayerGameStatuses[player.UserId], Is.EqualTo(PlayerGameStatus.NotReadyToPlay));
            ticker.ToggleReadyAll(true);
            Assert.That(ticker.PlayerGameStatuses[player.UserId], Is.EqualTo(PlayerGameStatus.NotReadyToPlay));
            ticker.MakeJoinGame(player, EntityUid.Invalid);
            Assert.That(player.AttachedEntity, Is.Null);
        });
        await pair.RunTicksSync(2);
        await pair.SelectMinimumSkillsForLobbyPlayers();
        await pair.Server.WaitAssertion(() =>
        {
            var ticker = pair.Server.System<GameTicker>();
            var player = pair.Player!;
            Assert.That(ticker.GetPlayerProfile(player).SkillPointsSpent, Is.EqualTo(ProfessionalSkillRules.MinimumSpent));
            ticker.ToggleReady(player, true);
            Assert.That(ticker.PlayerGameStatuses[player.UserId], Is.EqualTo(PlayerGameStatus.ReadyToPlay));
            ticker.ToggleReady(player, false);
            Assert.That(ticker.PlayerGameStatuses[player.UserId], Is.EqualTo(PlayerGameStatus.NotReadyToPlay));
            TestContext.Out.WriteLine("Skill-selection gate: zero points blocked, one point accepted, unready allowed.");
        });
        await pair.CleanReturnAsync();
    }
}

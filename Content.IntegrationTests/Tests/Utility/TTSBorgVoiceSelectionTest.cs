using System.Linq;
using System.Reflection;
using Content.Server.Chat.Systems;
using Content.Shared._Goobstation.Languages;
using Content.Shared.Corvax.TTS;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class TTSBorgVoiceSelectionTest
{
    [Test]
    public async Task VoiceChangesOnlyOnTheControlledBorgAndRejectsRestrictedPrototypes()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var players = pair.Server.ResolveDependency<IPlayerManager>();
            var prototypes = pair.Server.ResolveDependency<IPrototypeManager>();
            var chat = entities.System<ChatSystem>();
            var session = players.Sessions.Single();
            var previous = session.AttachedEntity;
            var borg = entities.SpawnEntity("BorgChassisGeneric", map.GridCoords);
            var other = entities.SpawnEntity("BorgChassisGeneric", map.GridCoords);
            var human = entities.SpawnEntity("MobHumanDummy", map.GridCoords);
            var original = entities.GetComponent<TTSComponent>(borg).VoicePrototypeId;
            var otherVoice = entities.GetComponent<TTSComponent>(other).VoicePrototypeId;
            var humanVoice = entities.GetComponent<TTSComponent>(human).VoicePrototypeId;
            var allowed = prototypes.EnumeratePrototypes<TTSVoicePrototype>().First(v => v.RoundStart && v.ID != original);
            var restricted = prototypes.EnumeratePrototypes<TTSVoicePrototype>().First(v => !v.RoundStart);
            var select = typeof(ChatSystem).GetMethod("OnLanguageMenuVoiceSelect", BindingFlags.Instance | BindingFlags.NonPublic)!;
            try
            {
                players.SetAttachedEntity(session, human);
                Select(allowed.ID);
                Assert.That(entities.GetComponent<TTSComponent>(human).VoicePrototypeId, Is.EqualTo(humanVoice));
                players.SetAttachedEntity(session, borg);
                Select("missing-voice");
                Select("");
                Select(restricted.ID);
                Assert.That(entities.GetComponent<TTSComponent>(borg).VoicePrototypeId, Is.EqualTo(original));
                Select(allowed.ID);
                Assert.That(entities.GetComponent<TTSComponent>(borg).VoicePrototypeId, Is.EqualTo(allowed.ID));
                Assert.That(entities.GetComponent<TTSComponent>(other).VoicePrototypeId, Is.EqualTo(otherVoice));
            }
            finally
            {
                players.SetAttachedEntity(session, previous);
            }

            void Select(string voice) => select.Invoke(chat,
                new object[] { new LanguageMenuVoiceSelectEvent(voice), new EntitySessionEventArgs(session) });
        });
        await pair.CleanReturnAsync();
    }
}

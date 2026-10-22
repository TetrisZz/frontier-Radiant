using System.Collections.Generic;
using System.Numerics;
using Content.Server.Chat.Systems;
using Content.Server.Corvax.TTS;
using Content.Server.Speech.EntitySystems;
using Content.Server.Telephone;
using Content.Shared.Corvax.TTS;
using Content.Shared.Mind.Components;
using Content.Shared.Speech;
using Content.Shared.Telephone;
using Robust.Shared.GameObjects;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class TTSHolopadRoutingTest
{
    public sealed class RelayRecorder : EntitySystem
    {
        public readonly HashSet<EntityUid> Relays = new();

        public override void Initialize()
        {
            SubscribeLocalEvent<EntitySpokeEvent>(OnSpoke,
                after: new[] { typeof(ListeningSystem) }, before: new[] { typeof(TTSSystem) });
        }

        private void OnSpoke(EntitySpokeEvent ev)
        {
            Relays.Clear();
            Relays.UnionWith(ev.AudioRelays);
        }
    }

    [Test]
    public async Task TelephoneRelayUsesOriginalUtteranceWithoutMutatingHologramVoice()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var telephone = entities.System<TelephoneSystem>();
            var recorder = entities.System<RelayRecorder>();
            var user = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<MindContainerComponent>(user);
            entities.AddComponent<SpeechComponent>(user);
            entities.AddComponent<TTSComponent>(user);
            var sourceUid = entities.SpawnEntity(null, map.GridCoords);
            var source = new Entity<TelephoneComponent>(sourceUid, entities.AddComponent<TelephoneComponent>(sourceUid));
            var remoteCoords = map.GridCoords.Offset(new Vector2(20, 0));
            var receiverUid = entities.SpawnEntity(null, remoteCoords);
            var receiver = new Entity<TelephoneComponent>(receiverUid, entities.AddComponent<TelephoneComponent>(receiverUid));
            typeof(TelephoneComponent).GetField(nameof(TelephoneComponent.SpeakerVolume))!
                .SetValue(receiver.Comp, TelephoneVolume.Speak);
            var hologram = entities.SpawnEntity(null, remoteCoords);
            var speech = entities.AddComponent<SpeechComponent>(hologram);
            telephone.SetSpeakerForTelephone(receiver, (hologram, speech));
            telephone.CallTelephone(source, receiver, user, new TelephoneCallOptions { ForceConnect = true });

            Speak("normal", null);
            Assert.That(recorder.Relays, Is.EquivalentTo(new[] { hologram }));
            Assert.That(entities.HasComponent<TTSComponent>(hologram), Is.False,
                "Shared hologram state must never be overwritten with a caller's voice");
            Speak("secret", "...");
            Assert.That(recorder.Relays, Is.Empty, "Whispers must not produce remote TTS");
            telephone.EndTelephoneCall(source, receiver);
            Speak("disconnected", null);
            Assert.That(recorder.Relays, Is.Empty, "A disconnected pad cannot receive speech audio");

            void Speak(string text, string? obfuscated)
            {
                var ev = new EntitySpokeEvent(user, text, null, obfuscated);
                entities.EventBus.RaiseLocalEvent(user, ev, true);
            }
        });
        await pair.CleanReturnAsync();
    }
}

using System.Collections.Generic;
using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Radio;
using Content.Server.Radio.Components;
using Content.Server.Radio.EntitySystems;
using Content.Shared.Chat;
using Content.Shared.Radio;
using Content.Shared.Radio.Components;
using Robust.Shared.GameObjects;
using Robust.Shared.Prototypes;

namespace Content.IntegrationTests.Tests.Utility;

[TestFixture]
public sealed class TTSRadioRoutingTest
{
    public sealed class DeliveryRecorder : EntitySystem
    {
        public readonly List<RadioMessageDeliveredEvent> Deliveries = new();
        public EntityUid? Blocked;
        public bool BlockTransmission;

        public override void Initialize()
        {
            SubscribeLocalEvent<RadioMessageDeliveredEvent>(OnDelivered);
            SubscribeLocalEvent<RadioReceiveAttemptEvent>(OnReceiveAttempt);
            SubscribeLocalEvent<RadioSendAttemptEvent>(OnSendAttempt);
        }

        private void OnDelivered(ref RadioMessageDeliveredEvent ev) => Deliveries.Add(ev);

        private void OnReceiveAttempt(ref RadioReceiveAttemptEvent ev)
        {
            if (Blocked == ev.RadioReceiver)
                ev.Cancelled = true;
        }

        private void OnSendAttempt(ref RadioSendAttemptEvent ev)
        {
            if (BlockTransmission)
                ev.Cancelled = true;
        }
    }

    [Test]
    public async Task TtsAudienceUsesActualFrequencyChannelAndInterferenceChecks()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings { Connected = true, Dirty = true });
        var map = await pair.CreateTestMap();
        await pair.Server.WaitAssertion(() =>
        {
            var entities = pair.Server.ResolveDependency<IEntityManager>();
            var radio = entities.System<RadioSystem>();
            var recorder = entities.System<DeliveryRecorder>();
            recorder.Deliveries.Clear();
            recorder.BlockTransmission = false;
            var source = entities.SpawnEntity(null, map.GridCoords);
            entities.AddComponent<TelecomExemptComponent>(source);
            var heard = Receiver(1459);
            var wrongFrequency = Receiver(1460);
            var wrongChannel = Receiver(1459, "Command");
            var blocked = Receiver(1459);
            recorder.Blocked = blocked;

            radio.SendRadioMessage(source, "TTS routing", SharedChatSystem.CommonChannel, source,
                frequency: 1459, language: "Test native language", isWhisper: true);
            var delivered = recorder.Deliveries.Single();
            Assert.That(delivered.Receivers, Does.Contain(heard));
            Assert.That(delivered.Receivers, Does.Not.Contain(wrongFrequency));
            Assert.That(delivered.Receivers, Does.Not.Contain(wrongChannel));
            Assert.That(delivered.Receivers, Does.Not.Contain(blocked));
            Assert.That(delivered.Message.Language, Is.EqualTo("Test native language"));
            Assert.That(delivered.Message.MessageSource, Is.EqualTo(source));
            Assert.That(delivered.Message.IsWhisper, Is.True, "Whisper metadata must reach the TTS delivery filter");
            Assert.That(delivered.Message.IsAnnouncement, Is.True, "Device notifications default to the automated announcer");

            recorder.Deliveries.Clear();
            entities.AddComponent<IntrinsicRadioTransmitterComponent>(source);
            var sourceMicrophone = entities.AddComponent<RadioMicrophoneComponent>(source);
            typeof(RadioMicrophoneComponent).GetField(nameof(RadioMicrophoneComponent.Frequency))!.SetValue(sourceMicrophone, 1459);
            var channel = pair.Server.ResolveDependency<IPrototypeManager>().Index<RadioChannelPrototype>(SharedChatSystem.CommonChannel);
            var speech = new EntitySpokeEvent(source, "Prefixed radio TTS", channel, "...");
            entities.EventBus.RaiseLocalEvent(source, speech, true);
            var prefixed = recorder.Deliveries.Single();
            Assert.That(prefixed.Receivers, Does.Contain(heard));
            Assert.That(prefixed.Message.IsWhisper, Is.False,
                "Radio speech is locally quiet, but must remain eligible for TTS at receiving radios");
            Assert.That(prefixed.Message.IsAnnouncement, Is.False, "Live speech must keep the player's selected voice");
            Assert.That(speech.Channel, Is.Null, "The transmitter should consume the radio channel");

            recorder.Deliveries.Clear();
            recorder.BlockTransmission = true;
            radio.SendRadioMessage(source, "Blocked TTS routing", SharedChatSystem.CommonChannel, source, frequency: 1459);
            Assert.That(recorder.Deliveries.Single().Receivers, Is.Empty, "A blocked transmission must not produce radio TTS");

            EntityUid Receiver(int frequency, string? channel = null)
            {
                var uid = entities.SpawnEntity(null, map.GridCoords);
                entities.AddComponent<ActiveRadioComponent>(uid).Channels.Add(channel ?? SharedChatSystem.CommonChannel);
                var microphone = entities.AddComponent<RadioMicrophoneComponent>(uid);
                typeof(RadioMicrophoneComponent).GetField(nameof(RadioMicrophoneComponent.Frequency))!.SetValue(microphone, frequency);
                return uid;
            }
        });
        await pair.CleanReturnAsync();
    }
}

using System.Reflection;
using Content.Server.Chat.Systems;
using Content.Server.Corvax.TTS;
using Content.Server.Radio;
using Content.Shared.Chat;
using Content.Shared.Radio;
using Content.Shared.Speech;
using Content.Shared.Telephone;
using NUnit.Framework;
using Robust.Shared.GameObjects;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class TTSSpeechRoutingTest
{
    [Test]
    public void RadioPrefixIsNotAnActualWhisperEvenAfterItsChannelIsConsumed()
    {
        var radio = new EntitySpokeEvent(new EntityUid(1), "radio message", new RadioChannelPrototype(), "...");
        Assert.That(radio.IsWhisper, Is.False);
        radio.Channel = null;
        Assert.That(radio.IsWhisper, Is.False, "A headset clearing Channel must not turn radio speech into a whisper");

        var whispered = new EntitySpokeEvent(new EntityUid(1), "secret", null, "...");
        Assert.That(whispered.IsWhisper, Is.True);
    }

    [TestCase(InGameICChatType.Whisper, SharedChatSystem.WhisperClearRange)]
    [TestCase(InGameICChatType.Speak, SharedChatSystem.VoiceRange)]
    public void QuietRadioSpeakerRemainsAudibleWithinItsOutputRange(InGameICChatType mode, float expectedRange)
    {
        Assert.That(TTSSystem.GetRadioSpeakerRange(mode), Is.EqualTo(expectedRange));
        Assert.That(TTSSystem.GetRadioSpeakerRange(mode), Is.GreaterThan(0));
    }

    [Test]
    public void RadioWhisperHasNoExtraLocalTtsCopy()
    {
        var speech = new EntitySpokeEvent(new EntityUid(1), "radio message", new RadioChannelPrototype(), "...");
        Assert.DoesNotThrow(() => typeof(TTSSystem).GetMethod("OnEntitySpoke", Flags)!.Invoke(EnabledSystem(), [speech]));
    }

    [TestCase("secret")]
    [TestCase("")]
    public void WhisperStopsBeforeVoiceLookupOrGeneration(string obfuscated)
    {
        var system = EnabledSystem();
        var speech = new EntitySpokeEvent(new EntityUid(1), "secret", null, obfuscated);
        // No entity manager is injected: a lookup, relay or synthesis would fail this test.
        Assert.DoesNotThrow(() => typeof(TTSSystem).GetMethod("OnEntitySpoke", Flags)!.Invoke(system, [speech]));
    }

    [Test]
    public void WhisperForwardedToRadioNeverGeneratesAudio()
    {
        var system = EnabledSystem();
        var message = new RadioReceiveEvent("secret", new EntityUid(1), null!, new EntityUid(2), null!, IsWhisper: true);
        var delivered = new RadioMessageDeliveredEvent(message, [new EntityUid(3)]);
        Assert.DoesNotThrow(() => typeof(TTSSystem).GetMethod("OnRadioDelivered", Flags)!.Invoke(system, [delivered]));
    }

    [Test]
    public void HolopadRelaysBelongToOriginalUtteranceNotSharedEmitterVoice()
    {
        var first = new EntitySpokeEvent(new EntityUid(1), "first", null, null);
        var second = new EntitySpokeEvent(new EntityUid(2), "second", null, null);
        var heard = new ListenEvent(first.Message, first.Source, audioRelays: first.AudioRelays);
        var received = new TelephoneMessageReceivedEvent(heard.Message, null!, heard.Source, default,
            AudioRelays: heard.AudioRelays);
        var remoteHologram = new EntityUid(3);
        received.AudioRelays!.Add(remoteHologram);
        received.AudioRelays.Add(remoteHologram);
        Assert.That(first.AudioRelays, Is.EquivalentTo(new[] { remoteHologram }));
        Assert.That(second.AudioRelays, Is.Empty);
        Assert.That(heard.AudioRelays, Is.SameAs(first.AudioRelays));
    }

    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.NonPublic;
    private static TTSSystem EnabledSystem()
    {
        var system = new TTSSystem();
        typeof(TTSSystem).GetField("_isEnabled", Flags)!.SetValue(system, true);
        return system;
    }
}

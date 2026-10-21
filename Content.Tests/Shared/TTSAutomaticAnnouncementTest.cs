using System;
using System.IO;
using System.Reflection;
using Content.Client.Corvax.TTS;
using Content.Server.Chat.Systems;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Corvax.TTS;
using Moq;
using NUnit.Framework;
using Robust.Shared.Audio;
using Robust.Shared.Configuration;
using Robust.Shared.Prototypes;
using Robust.Shared.Player;
using ServerTTS = Content.Server.Corvax.TTS.TTSSystem;
using Content.Server.Radio;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class TTSAutomaticAnnouncementTest
{
    [Test]
    public void AutomaticRadioNotificationsUseFactCoreWithoutACharacterTtsComponent()
    {
        var voice = new TTSVoicePrototype();
        typeof(TTSVoicePrototype).GetProperty(nameof(TTSVoicePrototype.Speaker))!.SetValue(voice, "fact_core");
        var prototypes = new Mock<IPrototypeManager>();
        prototypes.Setup(p => p.TryIndex<TTSVoicePrototype>("Fact_core", out voice)).Returns(true);
        var system = new ServerTTS();
        const BindingFlags flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(ServerTTS).GetField("_prototypeManager", flags)!.SetValue(system, prototypes.Object);
        typeof(ServerTTS).GetField("_isEnabled", flags)!.SetValue(system, true);

        object[] speaker = [""];
        Assert.That(typeof(ServerTTS).GetMethod("TryGetAutomaticSpeaker", flags)!.Invoke(system, speaker), Is.True);
        Assert.That(speaker[0], Is.EqualTo("fact_core"));
        var message = new RadioReceiveEvent(new string('x', 450), default, null!, default, null!, IsAnnouncement: true);
        var delivered = new RadioMessageDeliveredEvent(message, []);
        // No entity manager is injected: looking for a character voice would fail.
        Assert.DoesNotThrow(() => typeof(ServerTTS).GetMethod("OnRadioDelivered", flags)!.Invoke(system, [delivered]));
        var announcement = new AutomaticAnnouncementEvent("Event warning", Filter.Empty(), null);
        Assert.DoesNotThrow(() => typeof(ServerTTS).GetMethod("OnAutomaticAnnouncement", flags)!.Invoke(system, [announcement]));
    }

    [TestCase(TTSPlaybackKind.Radio, false, 1f, 1f, 1f, false)]
    [TestCase(TTSPlaybackKind.Announcement, false, 1f, 1f, 1f, false)]
    [TestCase(TTSPlaybackKind.Radio, true, 0f, 1f, 1f, false)]
    [TestCase(TTSPlaybackKind.Announcement, true, 0f, 1f, 1f, false)]
    [TestCase(TTSPlaybackKind.Radio, true, 1f, 0f, 0f, true)]
    [TestCase(TTSPlaybackKind.Announcement, true, 1f, 0f, 0f, true)]
    public void AutomaticNotificationsHaveIndependentListenerControls(TTSPlaybackKind kind, bool enabled,
        float automaticVolume, float radioVolume, float announcementVolume, bool expected)
    {
        var system = Listener(enabled, automaticVolume, radioVolume, announcementVolume);
        var message = new PlayTTSEvent([1], kind: kind, automatic: true);
        Assert.That(Invoke(system, "IsEnabled", message), Is.EqualTo(expected));
    }

    [TestCase(TTSPlaybackKind.Local)]
    [TestCase(TTSPlaybackKind.Radio)]
    [TestCase(TTSPlaybackKind.Announcement)]
    [TestCase(TTSPlaybackKind.Preview)]
    public void MutingAutomaticNotificationsDoesNotMuteLiveSpeech(TTSPlaybackKind kind)
    {
        var system = Listener(false, 0f, 1f, 1f);
        Assert.That(Invoke(system, "IsEnabled", new PlayTTSEvent([1], kind: kind)), Is.True);
    }

    [TestCase(TTSPlaybackKind.Radio, -16.0103f)]
    [TestCase(TTSPlaybackKind.Announcement, -13.0103f)]
    public void AutomaticVolumeIsNotMultipliedByLiveSpeechVolume(TTSPlaybackKind kind, float expected)
    {
        var system = Listener(true, 0.5f, 0f, 0f);
        Assert.That(Invoke(system, "GetVolume", new PlayTTSEvent([1], kind: kind, automatic: true)),
            Is.EqualTo(expected).Within(0.001f));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void AutomaticFlagRoundTripsWithoutChangingPlaybackKind(bool automatic)
    {
        var serializer = new NetSerializer.Serializer(new[] { typeof(PlayTTSEvent) });
        using var stream = new MemoryStream();
        serializer.Serialize(stream, new PlayTTSEvent([1, 2], kind: TTSPlaybackKind.Radio,
            radioChannel: "Supply", automatic: automatic));
        stream.Position = 0;
        var restored = (PlayTTSEvent) serializer.Deserialize(stream);
        Assert.That(restored.Automatic, Is.EqualTo(automatic));
        Assert.That(restored.Kind, Is.EqualTo(TTSPlaybackKind.Radio));
        Assert.That(restored.RadioChannel, Is.EqualTo("Supply"));
    }

    [TestCase("/Audio/Announcements/power_off.ogg")]
    [TestCase("/Audio/Announcements/meteors.ogg")]
    [TestCase("/Audio/Announcements/attention.ogg")]
    [TestCase("/Audio/Announcements/intercept.ogg")]
    [TestCase("/Audio/Announcements/shuttlerecalled.ogg")]
    [TestCase("/Audio/_NF/Announcements/PocketSizedAndy/andy2_solar_flare_start.ogg")]
    [TestCase("/Audio/_NF/Announcements/PocketSizedAndy/andy2_solar_flare_end.ogg")]
    public void PrerecordedAnnouncementSpeechIsSuppressed(string path)
    {
        Assert.That(AnnouncementAudio.WithoutRecordedSpeech(new SoundPathSpecifier(path)), Is.Null);
    }

    [Test]
    public void DelayedPowerOnRecordingIsSuppressedToo()
    {
        Assert.That(AnnouncementAudio.WithoutRecordedSpeech(new SoundCollectionSpecifier("PowerOn")), Is.Null);
    }

    [TestCase("/Audio/Announcements/announce.ogg")]
    [TestCase("/Audio/Announcements/war.ogg")]
    [TestCase("/Audio/Misc/notice1.ogg")]
    [TestCase("/Audio/Effects/alert.ogg")]
    public void SignalSoundsAndTheirParametersArePreserved(string path)
    {
        var sound = new SoundPathSpecifier(path, AudioParams.Default.WithVolume(-7));
        Assert.That(AnnouncementAudio.WithoutRecordedSpeech(sound), Is.SameAs(sound));
    }

    private static object Invoke(TTSSystem system, string method, PlayTTSEvent message) =>
        typeof(TTSSystem).GetMethod(method, BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(system, [message])!;

    private static TTSSystem Listener(bool enabled, float automaticVolume, float radioVolume, float announcementVolume)
    {
        var cfg = new Mock<IConfigurationManager>();
        cfg.Setup(c => c.GetCVar(CCCVars.TTSVolume)).Returns(1f);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSAutomaticEnabled)).Returns(enabled);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSAutomaticVolume)).Returns(automaticVolume);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSRadioVolume)).Returns(radioVolume);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSAnnouncementVolume)).Returns(announcementVolume);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSMutedRadioChannels)).Returns("");
        var system = new TTSSystem();
        typeof(TTSSystem).GetField("_cfg", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(system, cfg.Object);
        return system;
    }
}

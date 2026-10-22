using System;
using System.Linq;
using System.Reflection;
using Content.Client.Corvax.TTS;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Corvax.TTS;
using Moq;
using NUnit.Framework;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using ClientTTS = Content.Client.Corvax.TTS.TTSSystem;
using ServerTTS = Content.Server.Corvax.TTS.TTSSystem;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class TTSPlaybackTest
{
    [TestCase("Common")]
    [TestCase("Traffic")]
    public void CommonAndTrafficRadioTtsAreEnabledByDefault(string channel)
    {
        Assert.That(ClientTTS.IsChannelMuted(CCCVars.TTSMutedRadioChannels.DefaultValue, channel), Is.False);
    }

    [TestCase(TTSPlaybackKind.Local, -10f)]
    [TestCase(TTSPlaybackKind.Radio, -13f)]
    [TestCase(TTSPlaybackKind.Announcement, -10f)]
    [TestCase(TTSPlaybackKind.Preview, -10f)]
    public void OnlyRadioGetsGentleThreeDecibelAttenuation(TTSPlaybackKind kind, float expected)
    {
        var cfg = new Mock<IConfigurationManager>();
        cfg.Setup(c => c.GetCVar(CCCVars.TTSVolume)).Returns(1f);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSRadioVolume)).Returns(1f);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSAnnouncementVolume)).Returns(1f);
        var system = new ClientTTS();
        typeof(ClientTTS).GetField("_cfg", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(system, cfg.Object);
        var volume = typeof(ClientTTS).GetMethod("GetVolume", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(system, [new PlayTTSEvent([1], kind: kind)]);
        Assert.That(volume, Is.EqualTo(expected).Within(0.001f));
    }

    [Test]
    public void LocalSpeechKeepsOrderWithoutBlockingOtherSpeakers()
    {
        var queue = new TTSPlaybackQueue();
        var first = new PlayTTSEvent([1], new NetEntity(1));
        var second = new PlayTTSEvent([2], new NetEntity(1));
        var other = new PlayTTSEvent([3], new NetEntity(2));
        queue.Enqueue(first, TimeSpan.Zero);
        queue.Enqueue(second, TimeSpan.Zero);
        queue.Enqueue(other, TimeSpan.Zero);
        Assert.That(queue.TryDequeue(TTSPlaybackQueue.GetLane(first), TimeSpan.Zero, out var result), Is.True);
        Assert.That(result, Is.SameAs(first));
        Assert.That(queue.TryDequeue(TTSPlaybackQueue.GetLane(other), TimeSpan.Zero, out result), Is.True);
        Assert.That(result, Is.SameAs(other));
        Assert.That(queue.TryDequeue(TTSPlaybackQueue.GetLane(second), TimeSpan.Zero, out result), Is.True);
        Assert.That(result, Is.SameAs(second));
        Assert.That(queue.Lanes, Is.Empty);
    }

    [Test]
    public void GlobalRadioAndAnnouncementsUseSeparateQueuesWithoutNeedingSpeakerInPvs()
    {
        var events = new[]
        {
            new PlayTTSEvent([1], new NetEntity(1)),
            new PlayTTSEvent([2], kind: TTSPlaybackKind.Radio, radioChannel: "Command"),
            new PlayTTSEvent([3], kind: TTSPlaybackKind.Announcement),
            new PlayTTSEvent([4], kind: TTSPlaybackKind.Preview),
        };
        var queue = new TTSPlaybackQueue();
        foreach (var message in events)
            queue.Enqueue(message, TimeSpan.Zero);
        Assert.That(queue.Lanes.Length, Is.EqualTo(4));
        foreach (var message in events)
        {
            Assert.That(queue.TryDequeue(TTSPlaybackQueue.GetLane(message), TimeSpan.Zero, out var result), Is.True);
            Assert.That(result, Is.SameAs(message));
        }
    }

    [Test]
    public void RadioUsesOneLaneEvenWhenMultipleIntercomsRelayIt()
    {
        var first = new PlayTTSEvent([1], new NetEntity(1), kind: TTSPlaybackKind.Radio);
        var second = new PlayTTSEvent([2], new NetEntity(2), kind: TTSPlaybackKind.Radio);
        Assert.That(TTSPlaybackQueue.GetLane(first), Is.EqualTo(TTSPlaybackQueue.GetLane(second)));
    }

    [Test]
    public void AnnouncementWaitsForConsoleSignalAndSurvivesOrdinarySpeechExpiry()
    {
        var queue = new TTSPlaybackQueue();
        var message = new PlayTTSEvent([1], kind: TTSPlaybackKind.Announcement, delay: TimeSpan.FromSeconds(3));
        queue.Enqueue(message, TimeSpan.Zero);
        Assert.That(queue.TryDequeue(TTSPlaybackQueue.GetLane(message), TimeSpan.FromSeconds(2), out _), Is.False);
        Assert.That(queue.TryDequeue(TTSPlaybackQueue.GetLane(message), TimeSpan.FromSeconds(20), out var result), Is.True);
        Assert.That(result, Is.SameAs(message));
    }

    [TestCase(TTSPlaybackKind.Local, 15)]
    [TestCase(TTSPlaybackKind.Radio, 15)]
    [TestCase(TTSPlaybackKind.Announcement, 120)]
    public void OldSpeechExpires(TTSPlaybackKind kind, int lifetime)
    {
        var queue = new TTSPlaybackQueue();
        var message = new PlayTTSEvent([1], kind: kind);
        queue.Enqueue(message, TimeSpan.Zero);
        Assert.That(queue.TryDequeue(TTSPlaybackQueue.GetLane(message), TimeSpan.FromSeconds(lifetime), out _), Is.False);
        Assert.That(queue.Lanes, Is.Empty);
    }

    [Test]
    public void SpamCannotGrowQueueBeyondLimitAndOldestPendingLineIsDropped()
    {
        var queue = new TTSPlaybackQueue();
        for (byte i = 0; i < 20; i++)
            queue.Enqueue(new PlayTTSEvent([i], new NetEntity(1)), TimeSpan.Zero);
        var lane = queue.Lanes.Single();
        for (byte i = 12; i < 20; i++)
        {
            Assert.That(queue.TryDequeue(lane, TimeSpan.Zero, out var result), Is.True);
            Assert.That(result!.Data[0], Is.EqualTo(i));
        }
        Assert.That(queue.TryDequeue(lane, TimeSpan.Zero, out _), Is.False);
    }

    [Test]
    public void LaneCountIsBoundedAndRoundResetClearsPendingAudio()
    {
        var queue = new TTSPlaybackQueue();
        for (var i = 1; i <= 100; i++)
            queue.Enqueue(new PlayTTSEvent([1], new NetEntity(i)), TimeSpan.Zero);
        Assert.That(queue.Lanes.Length, Is.EqualTo(TTSPlaybackQueue.MaximumLanes));
        queue.Clear();
        Assert.That(queue.Lanes, Is.Empty);
    }

    [TestCase("Common,Command", "Common", true)]
    [TestCase(" Common , CustomRadio ", "CustomRadio", true)]
    [TestCase("Common", "CommonLongRange", false)]
    [TestCase("", "Medical", false)]
    [TestCase("Common", null, false)]
    public void RadioFilterMatchesWholePrototypeIdsIncludingCustomChannels(string muted, string channel, bool expected)
    {
        Assert.That(ClientTTS.IsChannelMuted(muted, channel), Is.EqualTo(expected));
    }

    [TestCase(TTSPlaybackKind.Radio)]
    [TestCase(TTSPlaybackKind.Announcement)]
    [TestCase(TTSPlaybackKind.Local)]
    [TestCase(TTSPlaybackKind.Preview)]
    public void ZeroOverallVolumeDisablesEveryKindOfTts(TTSPlaybackKind kind)
    {
        var cfg = new Mock<IConfigurationManager>();
        cfg.Setup(c => c.GetCVar(CCCVars.TTSVolume)).Returns(0f);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSRadioVolume)).Returns(3f);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSAnnouncementVolume)).Returns(3f);
        var system = new ClientTTS();
        typeof(ClientTTS).GetField("_cfg", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(system, cfg.Object);
        Assert.That(typeof(ClientTTS).GetMethod("IsEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(system, [new PlayTTSEvent([1], kind: kind)]), Is.False);
    }

    [TestCase(TTSPlaybackKind.Local, 0, 0, "", true)]
    [TestCase(TTSPlaybackKind.Radio, 0, 1, "", false)]
    [TestCase(TTSPlaybackKind.Radio, 1, 0, "", true)]
    [TestCase(TTSPlaybackKind.Radio, 1, 1, "Command", false)]
    [TestCase(TTSPlaybackKind.Radio, 1, 1, "Common", true)]
    [TestCase(TTSPlaybackKind.Announcement, 1, 0, "", false)]
    [TestCase(TTSPlaybackKind.Announcement, 0, 1, "Command", true)]
    public void RadioAndAnnouncementControlsAreIndependent(TTSPlaybackKind kind, float radioVolume,
        float announcementVolume, string muted, bool expected)
    {
        var cfg = new Mock<IConfigurationManager>();
        cfg.Setup(c => c.GetCVar(CCCVars.TTSVolume)).Returns(1f);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSRadioVolume)).Returns(radioVolume);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSAnnouncementVolume)).Returns(announcementVolume);
        cfg.Setup(c => c.GetCVar(CCCVars.TTSMutedRadioChannels)).Returns(muted);
        var system = new ClientTTS();
        typeof(ClientTTS).GetField("_cfg", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(system, cfg.Object);
        Assert.That(typeof(ClientTTS).GetMethod("IsEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(system, [new PlayTTSEvent([1], kind: kind, radioChannel: "Command")]), Is.EqualTo(expected));
    }

    [Test]
    public void OneListenerMutingRadioDoesNotChangeWhatAnotherListenerHears()
    {
        var muted = Listener(0f);
        var audible = Listener(1f);
        var message = new PlayTTSEvent([1, 2, 3], kind: TTSPlaybackKind.Radio, radioChannel: "Command");
        var enabled = typeof(ClientTTS).GetMethod("IsEnabled", BindingFlags.Instance | BindingFlags.NonPublic)!;
        Assert.That(enabled.Invoke(muted, [message]), Is.False);
        Assert.That(enabled.Invoke(audible, [message]), Is.True);
        Assert.That(message.Data, Is.EqualTo(new byte[] { 1, 2, 3 }));

        static ClientTTS Listener(float volume)
        {
            var cfg = new Mock<IConfigurationManager>();
            cfg.Setup(c => c.GetCVar(CCCVars.TTSVolume)).Returns(1f);
            cfg.Setup(c => c.GetCVar(CCCVars.TTSRadioVolume)).Returns(volume);
            cfg.Setup(c => c.GetCVar(CCCVars.TTSMutedRadioChannels)).Returns("");
            var system = new ClientTTS();
            typeof(ClientTTS).GetField("_cfg", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(system, cfg.Object);
            return system;
        }
    }

    [TestCase("ID", "Ай Ди")]
    [TestCase("GPS", "Джи Пи Эс")]
    [TestCase("c4", "Си четыре")]
    [TestCase("РНД", "Эр Эн Дэ")]
    [TestCase("Первая\nвторая", "Первая вторая")]
    public void AbbreviationsAreExpandedBeforeTransliterationAndLinesDoNotMerge(string text, string expected)
    {
        var sanitized = typeof(ServerTTS).GetMethod("Sanitize", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(new ServerTTS(), [text]);
        Assert.That(sanitized, Is.EqualTo(expected));
    }
}

using System.IO;
using Content.Shared._Goobstation.Languages;
using Content.Shared.Corvax.TTS;
using NUnit.Framework;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class TTSBorgVoiceTest
{
    [TestCase(false, null)]
    [TestCase(true, "Caitlyn")]
    public void LanguageMenuTransmitsBorgVoiceState(bool canSelect, string? voice)
    {
        var serializer = new NetSerializer.Serializer(new[] { typeof(LanguageMenuStateEvent) });
        var state = new LanguageMenuStateEvent("Binary", true, true, true, canSelect, voice);
        using var stream = new MemoryStream();
        serializer.Serialize(stream, state);
        stream.Position = 0;
        var restored = (LanguageMenuStateEvent) serializer.Deserialize(stream);
        Assert.That(restored.CanSelectVoice, Is.EqualTo(canSelect));
        Assert.That(restored.VoiceId, Is.EqualTo(voice));
        Assert.That(restored.NativeLanguage, Is.EqualTo("Binary"));
        Assert.That(restored.NativeSelected, Is.True);
    }

    [Test]
    public void VoiceSelectionRequestContainsNoTargetEntityOrEffect()
    {
        var serializer = new NetSerializer.Serializer(new[] { typeof(LanguageMenuVoiceSelectEvent) });
        using var stream = new MemoryStream();
        serializer.Serialize(stream, new LanguageMenuVoiceSelectEvent("Caitlyn"));
        stream.Position = 0;
        Assert.That(((LanguageMenuVoiceSelectEvent) serializer.Deserialize(stream)).VoiceId, Is.EqualTo("Caitlyn"));
        Assert.That(typeof(LanguageMenuVoiceSelectEvent).GetProperty("Target"), Is.Null);
        Assert.That(typeof(LanguageMenuVoiceSelectEvent).GetProperty("Effect"), Is.Null);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RobotPreviewFlagSurvivesNetworkSerialization(bool robotic)
    {
        var serializer = new NetSerializer.Serializer(new[] { typeof(RequestPreviewTTSEvent) });
        using var stream = new MemoryStream();
        serializer.Serialize(stream, new RequestPreviewTTSEvent("Caitlyn", robotic));
        stream.Position = 0;
        var restored = (RequestPreviewTTSEvent) serializer.Deserialize(stream);
        Assert.That(restored.VoiceId, Is.EqualTo("Caitlyn"));
        Assert.That(restored.Robotic, Is.EqualTo(robotic));
    }
}

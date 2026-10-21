using System.Linq;
using Content.Client.Corvax.TTS;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Corvax.TTS;
using NUnit.Framework;
using Robust.Shared.Configuration;

namespace Content.Tests.Shared;

[TestFixture]
public sealed class TTSDeviceAudioTest
{
    [Test]
    public void AnnouncementsUseIntercomWhileRadioKeepsItsExistingEffect()
    {
        Assert.That(CCCVars.TTSAnnouncementEffect.DefaultValue, Is.EqualTo("intercom"));
        Assert.That(CCCVars.TTSRadioEffect.DefaultValue, Is.EqualTo("radio_headset"));
    }

    [Test]
    public void ClientDoesNotReferenceExternalVorbisDecoder()
    {
        var references = typeof(TTSSystem).Assembly.GetReferencedAssemblies().Select(a => a.Name);
        Assert.That(references, Does.Not.Contain("VorbisPizza"));
        Assert.That(references, Does.Not.Contain("NVorbis"));
        Assert.That(typeof(PlayTTSEvent).GetProperty("IsWhisper"), Is.Null);
    }

    [Test]
    public void ListeningPreferencesStayLocalAndCannotChangeOutgoingSpeech()
    {
        foreach (var cvar in new CVarDef[]
                 { CCCVars.TTSVolume, CCCVars.TTSRadioVolume, CCCVars.TTSAnnouncementVolume, CCCVars.TTSMutedRadioChannels,
                     CCCVars.TTSAutomaticEnabled, CCCVars.TTSAutomaticVolume })
        {
            Assert.That(cvar.Flags.HasFlag(CVar.CLIENTONLY), Is.True, cvar.Name);
            Assert.That(cvar.Flags.HasFlag(CVar.REPLICATED), Is.False, cvar.Name);
        }
        Assert.That(typeof(CCCVars).GetField("TTSVoiceEffect"), Is.Null);
        foreach (var cvar in new CVarDef[]
                 { CCCVars.TTSRadioEffect, CCCVars.TTSAnnouncementEffect, CCCVars.TTSRetryCount, CCCVars.TTSRetryDelay })
            Assert.That(cvar.Flags.HasFlag(CVar.SERVERONLY), Is.True, cvar.Name);
    }
}

using Robust.Shared.Audio;

namespace Content.Server.Chat.Systems;

/// <summary>
/// Recorded announcement speech is replaced by TTS; non-vocal notification sounds remain unchanged.
/// </summary>
public static class AnnouncementAudio
{
    private static readonly HashSet<string> RecordedSpeech = new()
    {
        "/Audio/Announcements/aliens.ogg",
        "/Audio/Announcements/attention.ogg",
        "/Audio/Announcements/intercept.ogg",
        "/Audio/Announcements/ion_storm.ogg",
        "/Audio/Announcements/meteors.ogg",
        "/Audio/Announcements/outbreak7.ogg",
        "/Audio/Announcements/power_off.ogg",
        "/Audio/Announcements/power_on.ogg",
        "/Audio/Announcements/radiation.ogg",
        "/Audio/Announcements/shuttle_dock.ogg",
        "/Audio/Announcements/shuttlecalled.ogg",
        "/Audio/Announcements/shuttlerecalled.ogg",
        "/Audio/Announcements/welcome.ogg",
    };

    public static SoundSpecifier? WithoutRecordedSpeech(SoundSpecifier? sound)
    {
        if (sound is SoundCollectionSpecifier { Collection: "PowerOn" })
            return null;
        if (sound is SoundPathSpecifier path &&
            (RecordedSpeech.Contains(path.Path.ToString()) ||
             path.Path.ToString().StartsWith("/Audio/_NF/Announcements/", StringComparison.Ordinal)))
            return null;
        return sound;
    }
}

using Robust.Shared.Configuration;

namespace Content.Client._radiant.CCVar;

[CVarDefs]
public static class RadiantClientCCVars
{
    /// <summary>
    /// Whether jukeboxes and boomboxes can be heard by this client.
    /// </summary>
    public static readonly CVarDef<bool> JukeboxMusicEnabled =
        CVarDef.Create("audio.jukebox_music_enabled", true, CVar.ARCHIVE | CVar.CLIENTONLY);
}

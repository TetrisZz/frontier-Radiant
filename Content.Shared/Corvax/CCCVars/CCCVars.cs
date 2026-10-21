using Robust.Shared.Configuration;

namespace Content.Shared.Corvax.CCCVars;

/// <summary>
///     Corvax modules console variables
/// </summary>
[CVarDefs]
// ReSharper disable once InconsistentNaming
public sealed class CCCVars
{
    /**
     * TTS (Text-To-Speech)
     */

    /// <summary>
    /// URL of the TTS server API.
    /// </summary>
    public static readonly CVarDef<bool> TTSEnabled =
        CVarDef.Create("tts.enabled", false, CVar.SERVER | CVar.REPLICATED | CVar.ARCHIVE);

    /// <summary>
    /// URL of the TTS server API.
    /// </summary>
    public static readonly CVarDef<string> TTSApiUrl =
        CVarDef.Create("tts.api_url", "", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Auth token of the TTS server API.
    /// </summary>
    public static readonly CVarDef<string> TTSApiToken =
        CVarDef.Create("tts.api_token", "", CVar.SERVERONLY | CVar.CONFIDENTIAL);

    /// <summary>
    /// Amount of seconds before timeout for API
    /// </summary>
    public static readonly CVarDef<int> TTSApiTimeout =
        CVarDef.Create("tts.api_timeout", 5, CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> TTSRadioEffect =
        CVarDef.Create("tts.radio_effect", "radio_headset", CVar.SERVERONLY | CVar.ARCHIVE);

    public static readonly CVarDef<string> TTSAnnouncementEffect =
        CVarDef.Create("tts.announcement_effect", "intercom", CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>Additional attempts after the initial synthesis request.</summary>
    public static readonly CVarDef<int> TTSRetryCount =
        CVarDef.Create("tts.retry_count", 2, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>Seconds between a failed attempt and its retry.</summary>
    public static readonly CVarDef<float> TTSRetryDelay =
        CVarDef.Create("tts.retry_delay", 0.5f, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Default volume setting of TTS sound
    /// </summary>
    public static readonly CVarDef<float> TTSVolume =
        CVarDef.Create("tts.volume", 1.2f, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<float> TTSRadioVolume =
        CVarDef.Create("tts.radio_volume", 1.2f, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<float> TTSAnnouncementVolume =
        CVarDef.Create("tts.announcement_volume", 1.2f, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<bool> TTSAutomaticEnabled =
        CVarDef.Create("tts.automatic_enabled", true, CVar.CLIENTONLY | CVar.ARCHIVE);

    public static readonly CVarDef<float> TTSAutomaticVolume =
        CVarDef.Create("tts.automatic_volume", 1.2f, CVar.CLIENTONLY | CVar.ARCHIVE);

    // Store prototype IDs, rather than a fixed bitmask, to include custom radio channels.
    public static readonly CVarDef<string> TTSMutedRadioChannels =
        CVarDef.Create("tts.muted_radio_channels", "", CVar.CLIENTONLY | CVar.ARCHIVE);

    /// <summary>
    /// Count of in-memory cached tts voice lines.
    /// </summary>
    public static readonly CVarDef<int> TTSMaxCache =
        CVarDef.Create("tts.max_cache", 250, CVar.SERVERONLY | CVar.ARCHIVE);

    /// <summary>
    /// Tts rate limit values are accounted in periods of this size (seconds).
    /// After the period has passed, the count resets.
    /// </summary>
    public static readonly CVarDef<float> TTSRateLimitPeriod =
        CVarDef.Create("tts.rate_limit_period", 2f, CVar.SERVERONLY);

    /// <summary>
    /// How many tts preview messages are allowed in a single rate limit period.
    /// </summary>
    public static readonly CVarDef<int> TTSRateLimitCount =
        CVarDef.Create("tts.rate_limit_count", 2, CVar.SERVERONLY);

}

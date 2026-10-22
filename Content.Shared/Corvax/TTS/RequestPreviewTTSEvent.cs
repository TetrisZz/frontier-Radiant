using Robust.Shared.Serialization;

namespace Content.Shared.Corvax.TTS;

// ReSharper disable once InconsistentNaming
[Serializable, NetSerializable]
public sealed class RequestPreviewTTSEvent(string voiceId, bool robotic = false) : EntityEventArgs
{
    public string VoiceId { get; } = voiceId;
    public bool Robotic { get; } = robotic;
}

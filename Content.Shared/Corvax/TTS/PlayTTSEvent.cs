using Robust.Shared.Serialization;

namespace Content.Shared.Corvax.TTS;

[Serializable, NetSerializable]
// ReSharper disable once InconsistentNaming
public sealed class PlayTTSEvent : EntityEventArgs
{
    public byte[] Data { get; }
    public NetEntity? SourceUid { get; }
    public TTSPlaybackKind Kind { get; }
    public string? RadioChannel { get; }
    public TimeSpan Delay { get; }
    public bool Automatic { get; }

    public PlayTTSEvent(byte[] data, NetEntity? sourceUid = null,
        TTSPlaybackKind kind = TTSPlaybackKind.Local, string? radioChannel = null, TimeSpan delay = default, bool automatic = false)
    {
        Data = data;
        SourceUid = sourceUid;
        Kind = kind;
        RadioChannel = radioChannel;
        Delay = delay;
        Automatic = automatic;
    }
}

[Serializable, NetSerializable]
public enum TTSPlaybackKind : byte
{
    Local,
    Radio,
    Announcement,
    Preview,
}

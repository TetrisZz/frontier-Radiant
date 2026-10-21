using System.IO;
using System.Linq;
using Content.Shared.Chat;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Corvax.TTS;
using Content.Shared.GameTicking;
using Robust.Client.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Timing;

namespace Content.Client.Corvax.TTS;

public sealed class TTSSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IAudioManager _audioManager = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly TTSPlaybackQueue _queue = new();
    private readonly Dictionary<TTSPlaybackQueue.Lane, Playback> _playing = new();
    private ISawmill _sawmill = default!;

    private sealed record Playback(EntityUid Entity, AudioStream Stream, PlayTTSEvent Message);

    public override void Initialize()
    {
        UpdatesAfter.Add(typeof(AudioSystem));
        _sawmill = Logger.GetSawmill("tts");
        SubscribeNetworkEvent<PlayTTSEvent>(OnPlayTTS);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(_ => ClearPlayback());
    }

    public override void Shutdown()
    {
        ClearPlayback();
        base.Shutdown();
    }

    public void RequestPreviewTTS(string voiceId, bool robotic = false) =>
        RaiseNetworkEvent(new RequestPreviewTTSEvent(voiceId, robotic));

    private bool IsEnabled(PlayTTSEvent message)
    {
        if (_cfg.GetCVar(CCCVars.TTSVolume) <= 0)
            return false;
        if (message.Kind == TTSPlaybackKind.Radio &&
            IsChannelMuted(_cfg.GetCVar(CCCVars.TTSMutedRadioChannels), message.RadioChannel))
            return false;
        if (message.Automatic)
            return _cfg.GetCVar(CCCVars.TTSAutomaticEnabled) && _cfg.GetCVar(CCCVars.TTSAutomaticVolume) > 0;
        return message.Kind switch
        {
            TTSPlaybackKind.Radio => _cfg.GetCVar(CCCVars.TTSRadioVolume) > 0,
            TTSPlaybackKind.Announcement => _cfg.GetCVar(CCCVars.TTSAnnouncementVolume) > 0,
            _ => true,
        };
    }

    public static bool IsChannelMuted(string mutedChannels, string? channel) => channel != null &&
        mutedChannels.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Contains(channel, StringComparer.Ordinal);

    private void OnPlayTTS(PlayTTSEvent message)
    {
        if (IsEnabled(message) && message.Data.Length > 0)
            _queue.Enqueue(message, _timing.RealTime);
    }

    public override void FrameUpdate(float frameTime)
    {
        base.FrameUpdate(frameTime);
        if (_cfg.GetCVar(CCCVars.TTSVolume) <= 0)
        {
            ClearPlayback();
            return;
        }

        foreach (var (lane, playback) in _playing.ToArray())
        {
            if (TerminatingOrDeleted(playback.Entity) || !TryComp<AudioComponent>(playback.Entity, out var audio) ||
                !audio.Playing || !IsEnabled(playback.Message))
            {
                Release(playback);
                _playing.Remove(lane);
            }
            else
            {
                _audio.SetVolume(playback.Entity, GetVolume(playback.Message), audio);
            }
        }

        foreach (var lane in _queue.Lanes)
        {
            if (_playing.ContainsKey(lane) || _playing.Count >= TTSPlaybackQueue.MaximumLanes)
                continue;
            while (_queue.TryDequeue(lane, _timing.RealTime, out var message))
            {
                if (message != null && IsEnabled(message) && TryPlay(message, out var playback))
                {
                    _playing[lane] = playback;
                    break;
                }
            }
        }
    }

    private bool TryPlay(PlayTTSEvent message, out Playback playback)
    {
        playback = default!;
        EntityUid? source = null;
        if (message.SourceUid is { } netSource)
        {
            if (!TryGetEntity(netSource, out var entity) || entity is not { } uid || TerminatingOrDeleted(uid))
                return false;
            source = uid;
        }

        AudioStream? stream = null;
        (EntityUid Entity, AudioComponent Component)? audio = null;
        try
        {
            using var data = new MemoryStream(message.Data, false);
            stream = _audioManager.LoadAudioOggVorbis(data);
            var parameters = AudioParams.Default.WithVolume(GetVolume(message))
                .WithMaxDistance(SharedChatSystem.VoiceRange);
            audio = source is { } emitter
                ? _audio.PlayEntity(stream, emitter, null, parameters)
                : _audio.PlayGlobal(stream, null, parameters);
            if (audio == null)
            {
                stream.Dispose();
                return false;
            }

            playback = new Playback(audio.Value.Entity, stream, message);
            return true;
        }
        catch (Exception exception)
        {
            if (audio is { } playing && Exists(playing.Entity))
                Del(playing.Entity);
            stream?.Dispose();
            _sawmill.Warning($"Unable to play synthesized speech: {exception.Message}");
            return false;
        }
    }

    private void Release(Playback playback)
    {
        // The AL source must be detached before freeing its sample buffer.
        if (Exists(playback.Entity)) Del(playback.Entity);
        playback.Stream.Dispose();
    }

    private float GetVolume(PlayTTSEvent message)
    {
        var gain = _cfg.GetCVar(CCCVars.TTSVolume);
        gain *= message.Automatic ? _cfg.GetCVar(CCCVars.TTSAutomaticVolume) : message.Kind switch
        {
            TTSPlaybackKind.Radio => _cfg.GetCVar(CCCVars.TTSRadioVolume),
            TTSPlaybackKind.Announcement => _cfg.GetCVar(CCCVars.TTSAnnouncementVolume),
            _ => 1f,
        };
        var radioAttenuation = message.Kind == TTSPlaybackKind.Radio ? -3f : 0f;
        return -10f + SharedAudioSystem.GainToVolume(gain) + radioAttenuation;
    }

    private void ClearPlayback()
    {
        _queue.Clear();
        foreach (var playback in _playing.Values)
            Release(playback);
        _playing.Clear();
    }
}

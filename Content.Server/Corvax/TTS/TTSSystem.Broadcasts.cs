using System.Linq;
using Content.Server.Chat.Systems;
using Content.Server.Communications;
using Content.Server.Radio;
using Content.Server.Radio.Components;
using Content.Server.Station.Systems;
using Content.Shared.Chat;
using Content.Shared.Corvax.TTS;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Ghost;
using Content.Shared.Radio.Components;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.Station.Components;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Audio;
using Robust.Shared.Enums;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server.Corvax.TTS;

public sealed partial class TTSSystem
{
    private const string AutomaticAnnouncementVoice = "Fact_core";
    [Dependency] private readonly StationSystem _station = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private void InitializeBroadcasts()
    {
        SubscribeLocalEvent<CommunicationConsoleAnnouncementEvent>(OnConsoleAnnouncement);
        SubscribeLocalEvent<AutomaticAnnouncementEvent>(OnAutomaticAnnouncement);
        SubscribeLocalEvent<RadioMessageDeliveredEvent>(OnRadioDelivered);
    }

    private bool TryGetAutomaticSpeaker(out string speaker)
    {
        speaker = string.Empty;
        if (!_prototypeManager.TryIndex<TTSVoicePrototype>(AutomaticAnnouncementVoice, out var voice))
            return false;
        speaker = voice.Speaker;
        return true;
    }

    private void OnAutomaticAnnouncement(ref AutomaticAnnouncementEvent ev)
    {
        if (!_isEnabled || string.IsNullOrWhiteSpace(ev.Message) || !TryGetAutomaticSpeaker(out var speaker))
            return;
        QueueAnnouncement(FormattedMessage.RemoveMarkupPermissive(ev.Message), speaker, ev.Recipients, ev.Sound, true);
    }

    private bool TryGetSpeaker(EntityUid uid, out string speaker)
    {
        speaker = string.Empty;
        if (!TryComp<TTSComponent>(uid, out var tts) || tts.VoicePrototypeId is not { } voice)
            return false;

        var ev = new TransformSpeakerVoiceEvent(uid, voice);
        RaiseLocalEvent(uid, ev);
        if (!_prototypeManager.TryIndex<TTSVoicePrototype>(ev.VoiceId, out var prototype))
            return false;

        speaker = prototype.Speaker;
        return true;
    }

    private void OnConsoleAnnouncement(ref CommunicationConsoleAnnouncementEvent ev)
    {
        if (!_isEnabled || string.IsNullOrWhiteSpace(ev.Text) ||
            ev.Sender is not { } sender || !TryGetSpeaker(sender, out var speaker))
            return;

        Filter filter;
        if (ev.Component.Global)
            filter = Filter.Broadcast();
        else if (_station.GetOwningStation(ev.Uid) is { } station &&
                 TryComp<StationDataComponent>(station, out var data))
            filter = _station.GetInStation(data);
        else
            return;

        var sound = ev.Component.Global ? ev.Component.Sound : SharedChatSystem.DefaultAnnouncementSound;
        QueueAnnouncement(ev.Text, speaker, filter, AnnouncementAudio.WithoutRecordedSpeech(sound), false);
    }

    private async void QueueAnnouncement(string text, string speaker, Filter filter, SoundSpecifier? sound, bool automatic)
    {
        // Capture the same audience as the text, not whoever happens to be there after synthesis.
        var recipients = filter.Recipients.Where(session => session.Status == SessionStatus.InGame).ToArray();
        if (recipients.Length == 0)
            return;
        var generation = _roundGeneration;
        var readyAt = _timing.RealTime;
        if (sound != null)
            readyAt += _audio.GetAudioLength(_audio.ResolveSound(sound)) + TimeSpan.FromSeconds(0.25);
        var effect = _cfg.GetCVar(CCCVars.TTSAnnouncementEffect);
        var audio = await GenerateTTS(text, speaker, effect);
        if (audio == null || !_isEnabled || generation != _roundGeneration)
            return;

        var delay = readyAt > _timing.RealTime ? readyAt - _timing.RealTime : TimeSpan.Zero;
        var message = new PlayTTSEvent(audio, kind: TTSPlaybackKind.Announcement, delay: delay, automatic: automatic);
        foreach (var session in recipients)
        {
            if (session.Status == SessionStatus.InGame)
                RaiseNetworkEvent(message, Filter.SinglePlayer(session), recordReplay: false);
        }
    }

    private void OnRadioDelivered(ref RadioMessageDeliveredEvent ev)
    {
        var message = ev.Message;
        if (!_isEnabled || message.IsWhisper || !message.IsAnnouncement && message.Message.Length > MaxMessageChars)
            return;
        var hasSpeaker = message.IsAnnouncement
            ? TryGetAutomaticSpeaker(out var speaker)
            : TryGetSpeaker(message.MessageSource, out speaker);
        if (!hasSpeaker)
            return;

        // A headset and a nearby intercom must not produce two copies for the same listener.
        // Prefer the headset (non-positional) copy when both are available.
        var recipients = new Dictionary<ICommonSession, (EntityUid Listener, NetEntity? Source)>();
        foreach (var receiver in ev.Receivers)
        {
            if (!Exists(receiver))
                continue;

            var headset = HasComp<HeadsetComponent>(receiver);
            var listener = headset ? Transform(receiver).ParentUid : receiver;
            if ((headset || HasComp<IntrinsicRadioReceiverComponent>(receiver)) && TryComp<ActorComponent>(listener, out var actor))
                AddRecipient(actor.PlayerSession, null);

            if (receiver == message.RadioSource || !TryComp<RadioSpeakerComponent>(receiver, out var radio))
                continue;

            // Quiet device output changes its audible range, not whether radio speech has TTS.
            var range = GetRadioSpeakerRange(radio.OutputChatType);
            var position = _xforms.GetMapCoordinates(receiver);
            foreach (var session in Filter.Pvs(receiver).Recipients)
            {
                if (session.AttachedEntity is not { } nearby)
                    continue;
                var target = _xforms.GetMapCoordinates(nearby);
                if (position.MapId == target.MapId && (position.Position - target.Position).LengthSquared() <= range * range)
                    AddRecipient(session, GetNetEntity(receiver));
            }
        }

        if (recipients.Count > 0)
            SendRadio();

        void AddRecipient(ICommonSession session, NetEntity? source)
        {
            if (session.AttachedEntity is not { } listener || HasComp<GhostComponent>(listener) ||
                !_chatSystem.UnderstandsLanguage(listener, message.Language))
                return;
            if (!recipients.TryGetValue(session, out var old) || source == null && old.Source != null)
                recipients[session] = (listener, source);
        }

        async void SendRadio()
        {
            var generation = _roundGeneration;
            var effect = !message.IsAnnouncement && HasComp<BorgChassisComponent>(message.MessageSource)
                ? BorgSpeechEffect
                : _cfg.GetCVar(CCCVars.TTSRadioEffect);
            var text = message.IsAnnouncement ? FormattedMessage.RemoveMarkupPermissive(message.Message) : message.Message;
            var audio = await GenerateTTS(text, speaker, effect);
            if (audio == null || !_isEnabled || generation != _roundGeneration)
                return;

            foreach (var (session, recipient) in recipients)
            {
                if (session.Status != SessionStatus.InGame || session.AttachedEntity != recipient.Listener ||
                    HasComp<GhostComponent>(recipient.Listener) ||
                    !_chatSystem.UnderstandsLanguage(recipient.Listener, message.Language))
                    continue;
                RaiseNetworkEvent(new PlayTTSEvent(audio, recipient.Source, kind: TTSPlaybackKind.Radio,
                    radioChannel: message.Channel.ID, automatic: message.IsAnnouncement), Filter.SinglePlayer(session), recordReplay: false);
            }
        }
    }

    public static float GetRadioSpeakerRange(InGameICChatType outputType) =>
        outputType == InGameICChatType.Whisper ? SharedChatSystem.WhisperClearRange : SharedChatSystem.VoiceRange;
}

using System.Threading.Tasks;
using Content.Server.Chat.Systems;
using System.Linq;
using Content.Server.Speech.EntitySystems;
using Content.Shared.Corvax.CCCVars;
using Content.Shared.Corvax.TTS;
using Content.Shared.Silicons.Borgs.Components;
using Content.Shared.GameTicking;
using Content.Shared.Players.RateLimiting;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server.Corvax.TTS;

// ReSharper disable once InconsistentNaming
public sealed partial class TTSSystem : EntitySystem
{
    [Dependency] private readonly IConfigurationManager _cfg = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly TTSManager _ttsManager = default!;
    [Dependency] private readonly SharedTransformSystem _xforms = default!;
    [Dependency] private readonly IRobustRandom _rng = default!;
    [Dependency] private readonly ChatSystem _chatSystem = default!; // Radiant Sector: native language TTS routing.

    private readonly List<string> _sampleText =
        new()
        {
            "Съешь же ещё этих мягких французских булок, да выпей чаю.",
            "Клоун, прекрати разбрасывать банановые кожурки офицерам под ноги!",
            "Капитан, вы уверены что хотите назначить клоуна на должность главы персонала?",
            "Эс Бэ! Тут человек в сером костюме, с тулбоксом и в маске! Помогите!!",
            "Учёные, тут странная аномалия в баре! Она уже съела мима!",
            "Я надеюсь что инженеры внимательно следят за сингулярностью...",
            "Вы слышали эти странные крики в техах? Мне кажется туда ходить небезопасно.",
            "Вы не видели Гамлета? Мне кажется он забегал к вам на кухню.",
            "Здесь есть доктор? Человек умирает от отравленного пончика! Нужна помощь!",
            "Вам нужно согласие и печать квартирмейстера, если вы хотите сделать заказ на партию дробовиков.",
            "Возле эвакуационного шаттла разгерметизация! Инженеры, нам срочно нужна ваша помощь!",
            "Бармен, налей мне самого крепкого вина, которое есть в твоих запасах!"
        };

    private const int MaxMessageChars = 100 * 2; // same as SingleBubbleCharLimit * 2
    private const string BorgSpeechEffect = "robotic";
    private bool _isEnabled = false;
    private int _roundGeneration;

    public override void Initialize()
    {
        _cfg.OnValueChanged(CCCVars.TTSEnabled, v =>
        {
            if (_isEnabled && !v)
            {
                _roundGeneration++;
                _ttsManager.ResetCache();
            }
            _isEnabled = v;
        }, true);

        SubscribeLocalEvent<TransformSpeechEvent>(OnTransformSpeech);
        SubscribeLocalEvent<EntitySpokeEvent>(OnEntitySpoke, after: new[] { typeof(ListeningSystem) });
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestartCleanup);
        InitializeBroadcasts();

        SubscribeNetworkEvent<RequestPreviewTTSEvent>(OnRequestPreviewTTS);

        RegisterRateLimits();
    }

    private void OnRoundRestartCleanup(RoundRestartCleanupEvent ev)
    {
        _ttsManager.ResetCache();
        _roundGeneration++;
    }

    private async void OnRequestPreviewTTS(RequestPreviewTTSEvent ev, EntitySessionEventArgs args)
    {
        if (!_isEnabled ||
            !_prototypeManager.TryIndex<TTSVoicePrototype>(ev.VoiceId, out var protoVoice))
            return;

        if (ev.Robotic && (args.SenderSession.AttachedEntity is not { Valid: true } borg ||
            !HasComp<BorgChassisComponent>(borg)))
            return;

        if (HandleRateLimit(args.SenderSession) != RateLimitStatus.Allowed)
            return;

        var previewText = _rng.Pick(_sampleText);
        var generation = _roundGeneration;
        var soundData = await GenerateTTS(previewText, protoVoice.Speaker, ev.Robotic ? BorgSpeechEffect : null);
        if (soundData is null || !_isEnabled || generation != _roundGeneration ||
            args.SenderSession.Status != SessionStatus.InGame && args.SenderSession.Status != SessionStatus.Connected)
            return;

        RaiseNetworkEvent(new PlayTTSEvent(soundData, kind: TTSPlaybackKind.Preview),
            Filter.SinglePlayer(args.SenderSession), recordReplay: false);
    }

    private void OnEntitySpoke(EntitySpokeEvent args)
    {
        if (!_isEnabled || args.ObfuscatedMessage != null || args.Message.Length > MaxMessageChars ||
            !TryGetSpeaker(args.Source, out var speaker))
            return;

        HandleSay(args.Message, args.Language, speaker, args.AudioRelays.Append(args.Source).Distinct().ToArray(),
            HasComp<BorgChassisComponent>(args.Source) ? BorgSpeechEffect : null);
    }

    /// <summary>
    /// Radiant Sector: never send intelligible or replacement TTS to a listener who cannot understand the speech.
    /// </summary>
    private async void HandleSay(string message, string? language, string speaker, EntityUid[] emitters, string? effect)
    {
        var generation = _roundGeneration;
        var soundData = await GenerateTTS(message, speaker, effect);
        if (soundData is null || generation != _roundGeneration || !_isEnabled) return;

        // Generate once and use a positional emitter at each end of the call.
        // A listener close to both ends must still hear only one copy.
        var recipients = new Dictionary<ICommonSession, (EntityUid Emitter, float Distance)>();
        foreach (var emitter in emitters)
        {
            if (TerminatingOrDeleted(emitter)) continue;
            var position = _xforms.GetMapCoordinates(emitter);
            foreach (var session in Filter.Pvs(emitter).Recipients)
            {
                if (session.AttachedEntity is not { Valid: true } listener ||
                    !_chatSystem.UnderstandsLanguage(listener, language)) continue;
                var target = _xforms.GetMapCoordinates(listener);
                var distance = (position.Position - target.Position).LengthSquared();
                if (position.MapId != target.MapId || distance > ChatSystem.VoiceRange * ChatSystem.VoiceRange)
                    continue;
                if (!recipients.TryGetValue(session, out var old) || distance < old.Distance)
                    recipients[session] = (emitter, distance);
            }
        }
        foreach (var (session, recipient) in recipients)
            RaiseNetworkEvent(new PlayTTSEvent(soundData, GetNetEntity(recipient.Emitter)),
                Filter.SinglePlayer(session), recordReplay: false);
    }

    // ReSharper disable once InconsistentNaming
    private async Task<byte[]?> GenerateTTS(string text, string speaker, string? effect = null)
    {
        var textSanitized = Sanitize(text);
        if (textSanitized == "") return null;
        if (char.IsLetter(textSanitized[^1]))
            textSanitized += ".";

        return await _ttsManager.ConvertTextToSpeech(speaker, textSanitized, effect);
    }
}

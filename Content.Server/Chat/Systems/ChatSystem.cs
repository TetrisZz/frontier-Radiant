using System.Globalization;
using System.Linq;
using System.Text;
using Content.Server.Administration.Logs;
using Content.Server.Administration.Managers;
using Content.Server.Chat.Managers;
using Content.Server.GameTicking;
using Content.Server.Speech.EntitySystems;
using Content.Server.Speech.Prototypes;
using Content.Server.Station.Systems;
using Content.Shared.ActionBlocker;
using Content.Shared.Administration;
using Content.Shared.CCVar;
using Content.Shared.Chat;
using Content.Shared.Database;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared._radiant.Abilities.Shadowkin; // Radiant Sector
using Content.Shared._Goobstation.Languages; // Radiant Sector
using Content.Shared.Humanoid; // Radiant Sector
using Content.Shared.IdentityManagement;
using Content.Shared.Mobs.Systems;
using Content.Shared.Players;
using Content.Shared.Players.RateLimiting;
using Content.Shared.Radio;
using Content.Shared.Popups; // Radiant Sector
using Content.Shared.Station.Components;
using Content.Shared.Tag; // Radiant Sector
using Content.Shared.Whitelist;
using Robust.Server.Player;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Configuration;
using Robust.Shared.Console;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Replays;
using Robust.Shared.Utility;

namespace Content.Server.Chat.Systems;

// TODO refactor whatever active warzone this class and chatmanager have become
/// <summary>
///     ChatSystem is responsible for in-simulation chat handling, such as whispering, speaking, emoting, etc.
///     ChatSystem depends on ChatManager to actually send the messages.
/// </summary>
public sealed partial class ChatSystem : SharedChatSystem
{
    [Dependency] private readonly IReplayRecordingManager _replay = default!;
    [Dependency] private readonly IConfigurationManager _configurationManager = default!;
    [Dependency] private readonly IChatManager _chatManager = default!;
    [Dependency] private readonly IChatSanitizationManager _sanitizer = default!;
    [Dependency] private readonly IAdminManager _adminManager = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;
    [Dependency] private readonly ActionBlockerSystem _actionBlocker = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly MobStateSystem _mobStateSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly ReplacementAccentSystem _wordreplacement = default!;
    [Dependency] private readonly EntityWhitelistSystem _whitelistSystem = default!;
    [Dependency] private readonly ExamineSystemShared _examineSystem = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!; // Radiant Sector
    [Dependency] private readonly TagSystem _tagSystem = default!; // Radiant Sector

    private static readonly ProtoId<TagPrototype> ShadowkinEmotesTag = "ShadowkinEmotes"; // Radiant Sector

    // Radiant Sector: language choice belongs to the current character, not to a player account.
    // It is deliberately server-side only: a language setting must not be able to stop a client from loading.
    private readonly HashSet<EntityUid> _nativeLanguageSelected = new();

    private bool _loocEnabled = true;
    private bool _deadLoocEnabled;
    private bool _critLoocEnabled;
    private readonly bool _adminLoocEnabled = true;

    public override void Initialize()
    {
        base.Initialize();
        CacheEmotes();
        Subs.CVar(_configurationManager, CCVars.LoocEnabled, OnLoocEnabledChanged, true);
        Subs.CVar(_configurationManager, CCVars.DeadLoocEnabled, OnDeadLoocEnabledChanged, true);
        Subs.CVar(_configurationManager, CCVars.CritLoocEnabled, OnCritLoocEnabledChanged, true);

        SubscribeLocalEvent<GameRunLevelChangedEvent>(OnGameChange);
        SubscribeNetworkEvent<LanguageMenuRequestEvent>(OnLanguageMenuRequest); // Radiant Sector
        SubscribeNetworkEvent<LanguageMenuSelectEvent>(OnLanguageMenuSelect); // Radiant Sector
    }

    private void OnLoocEnabledChanged(bool val)
    {
        if (_loocEnabled == val) return;

        _loocEnabled = val;
        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(val ? "chat-manager-looc-chat-enabled-message" : "chat-manager-looc-chat-disabled-message"));
    }

    private void OnDeadLoocEnabledChanged(bool val)
    {
        if (_deadLoocEnabled == val) return;

        _deadLoocEnabled = val;
        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(val ? "chat-manager-dead-looc-chat-enabled-message" : "chat-manager-dead-looc-chat-disabled-message"));
    }

    private void OnCritLoocEnabledChanged(bool val)
    {
        if (_critLoocEnabled == val)
            return;

        _critLoocEnabled = val;
        _chatManager.DispatchServerAnnouncement(
            Loc.GetString(val ? "chat-manager-crit-looc-chat-enabled-message" : "chat-manager-crit-looc-chat-disabled-message"));
    }

    private void OnGameChange(GameRunLevelChangedEvent ev)
    {
        switch (ev.New)
        {
            case GameRunLevel.InRound:
                if (!_configurationManager.GetCVar(CCVars.OocEnableDuringRound))
                    _configurationManager.SetCVar(CCVars.OocEnabled, false);
                break;
            case GameRunLevel.PostRound:
            case GameRunLevel.PreRoundLobby:
                if (!_configurationManager.GetCVar(CCVars.OocEnableDuringRound))
                    _configurationManager.SetCVar(CCVars.OocEnabled, true);
                break;
        }
    }

    /// <summary>
    ///     Sends an in-character chat message to relevant clients.
    /// </summary>
    /// <param name="source">The entity that is speaking</param>
    /// <param name="message">The message being spoken or emoted</param>
    /// <param name="desiredType">The chat type</param>
    /// <param name="hideChat">Whether or not this message should appear in the chat window</param>
    /// <param name="hideLog">Whether or not this message should appear in the adminlog window</param>
    /// <param name="shell"></param>
    /// <param name="player">The player doing the speaking</param>
    /// <param name="nameOverride">The name to use for the speaking entity. Usually this should just be modified via <see cref="TransformSpeakerNameEvent"/>. If this is set, the event will not get raised.</param>
    public void TrySendInGameICMessage(
        EntityUid source,
        string message,
        InGameICChatType desiredType,
        bool hideChat, bool hideLog = false,
        IConsoleShell? shell = null,
        ICommonSession? player = null, string? nameOverride = null,
        bool checkRadioPrefix = true,
        bool ignoreActionBlocker = false)
    {
        TrySendInGameICMessage(source, message, desiredType, hideChat ? ChatTransmitRange.HideChat : ChatTransmitRange.Normal, hideLog, shell, player, nameOverride, checkRadioPrefix, ignoreActionBlocker);
    }

    /// <summary>
    ///     Sends an in-character chat message to relevant clients.
    /// </summary>
    /// <param name="source">The entity that is speaking</param>
    /// <param name="message">The message being spoken or emoted</param>
    /// <param name="desiredType">The chat type</param>
    /// <param name="range">Conceptual range of transmission, if it shows in the chat window, if it shows to far-away ghosts or ghosts at all...</param>
    /// <param name="shell"></param>
    /// <param name="player">The player doing the speaking</param>
    /// <param name="nameOverride">The name to use for the speaking entity. Usually this should just be modified via <see cref="TransformSpeakerNameEvent"/>. If this is set, the event will not get raised.</param>
    /// <param name="ignoreActionBlocker">If set to true, action blocker will not be considered for whether an entity can send this message.</param>
    public void TrySendInGameICMessage(
        EntityUid source,
        string message,
        InGameICChatType desiredType,
        ChatTransmitRange range,
        bool hideLog = false,
        IConsoleShell? shell = null,
        ICommonSession? player = null,
        string? nameOverride = null,
        bool checkRadioPrefix = true,
        bool ignoreActionBlocker = false
        )
    {
        if (HasComp<GhostComponent>(source))
        {
            // Ghosts can only send dead chat messages, so we'll forward it to InGame OOC.
            TrySendInGameOOCMessage(source, message, InGameOOCChatType.Dead, range == ChatTransmitRange.HideChat, shell, player);
            return;
        }

        if (player != null && _chatManager.HandleRateLimit(player) != RateLimitStatus.Allowed)
            return;

        // Sus
        if (player?.AttachedEntity is { Valid: true } entity && source != entity)
        {
            return;
        }

        if (!CanSendInGame(message, shell, player))
            return;

        ignoreActionBlocker = CheckIgnoreSpeechBlocker(source, ignoreActionBlocker);

        // this method is a disaster
        // every second i have to spend working with this code is fucking agony
        // scientists have to wonder how any of this was merged
        // coding any game admin feature that involves chat code is pure torture
        // changing even 10 lines of code feels like waterboarding myself
        // and i dont feel like vibe checking 50 code paths
        // so we set this here
        // todo free me from chat code
        if (player != null)
        {
            _chatManager.EnsurePlayer(player.UserId).AddEntity(GetNetEntity(source));
        }

        if (desiredType == InGameICChatType.Speak && message.StartsWith(LocalPrefix))
        {
            // prevent radios and remove prefix.
            checkRadioPrefix = false;
            message = message[1..];
        }

        // Radiant Sector: +э is a shadowkin-only empathy link. This has to run before
        // radio-prefix sanitization because the keycode is also used by a radio channel.
        if (desiredType == InGameICChatType.Speak && TrySendShadowkinEmpathy(source, message, nameOverride))
            return;

        bool shouldCapitalize = (desiredType != InGameICChatType.Emote);
        bool shouldPunctuate = _configurationManager.GetCVar(CCVars.ChatPunctuation);
        // Capitalizing the word I only happens in English, so we check language here
        bool shouldCapitalizeTheWordI = (!CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Parent.Name == "en")
            || (CultureInfo.CurrentCulture.IsNeutralCulture && CultureInfo.CurrentCulture.Name == "en");

        message = SanitizeInGameICMessage(source, message, out var emoteStr, shouldCapitalize, shouldPunctuate, shouldCapitalizeTheWordI);

        // Was there an emote in the message? If so, send it.
        if (player != null && emoteStr != message && emoteStr != null)
        {
            SendEntityEmote(source, emoteStr, range, nameOverride, ignoreActionBlocker);
        }

        // This can happen if the entire string is sanitized out.
        if (string.IsNullOrEmpty(message))
            return;

        // This message may have a radio prefix, and should then be whispered to the resolved radio channel
        if (checkRadioPrefix)
        {
            if (TryProccessRadioMessage(source, message, out var modMessage, out var channel))
            {
                SendEntityWhisper(source, modMessage, range, channel, nameOverride, hideLog, ignoreActionBlocker);
                return;
            }
        }

        // Otherwise, send whatever type.
        switch (desiredType)
        {
            case InGameICChatType.Speak:
                SendEntitySpeak(source, message, range, nameOverride, hideLog, ignoreActionBlocker);
                break;
            case InGameICChatType.Whisper:
                SendEntityWhisper(source, message, range, null, nameOverride, hideLog, ignoreActionBlocker);
                break;
            case InGameICChatType.Emote:
                SendEntityEmote(source, message, range, nameOverride, hideLog: hideLog, ignoreActionBlocker: ignoreActionBlocker);
                break;
        }
    }

    /// <summary>
    /// Handles the private communication channel shared by living shadowkin.
    /// </summary>
    private bool TrySendShadowkinEmpathy(EntityUid source, string input, string? nameOverride)
    {
        if (input.Length < 2 || input[0] != '+' || char.ToLowerInvariant(input[1]) != 'э')
            return false;

        if (!IsShadowkin(source))
        {
            // Preserve the existing +э radio behaviour for every other species.
            return false;
        }

        var message = SanitizeMessageReplaceWords(input[2..].Trim());
        message = SanitizeMessageCapital(message);
        if (_configurationManager.GetCVar(CCVars.ChatPunctuation))
            message = SanitizeMessagePeriod(message);

        if (string.IsNullOrWhiteSpace(message))
            return true;

        var name = FormattedMessage.EscapeText(nameOverride ?? Name(source));
        var cleanMessage = FormattedMessage.EscapeText(message);
        var wrapped = Loc.GetString("shadowkin-empathy-chat-wrap", ("name", name), ("message", cleanMessage));
        var recipients = new List<INetChannel>();

        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } target || !IsShadowkin(target))
            {
                continue;
            }

            recipients.Add(session.Channel);
        }

        _chatManager.ChatMessageToMany(ChatChannel.ShadowkinEmpathy, message, wrapped, source, false, true, recipients);
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Shadowkin empathy from {ToPrettyString(source):player}: {message}");
        return true;
    }

    // Radiant Sector: retain all three identifiers so the channel works for profiles
    // created before the ability component was added as well as newly spawned shadowkin.
    private bool IsShadowkin(EntityUid entity)
    {
        if (HasComp<ShadowkinShadeStepComponent>(entity))
            return true;

        if (TryComp(entity, out HumanoidAppearanceComponent? humanoid) && humanoid.Species == "Shadowkin")
            return true;

        return _tagSystem.HasTag(entity, ShadowkinEmotesTag);
    }

    public void TrySendInGameOOCMessage(
        EntityUid source,
        string message,
        InGameOOCChatType type,
        bool hideChat,
        IConsoleShell? shell = null,
        ICommonSession? player = null
        )
    {
        if (!CanSendInGame(message, shell, player))
            return;

        if (player != null && _chatManager.HandleRateLimit(player) != RateLimitStatus.Allowed)
            return;

        // It doesn't make any sense for a non-player to send in-game OOC messages, whereas non-players may be sending
        // in-game IC messages.
        if (player?.AttachedEntity is not { Valid: true } entity || source != entity)
            return;

        message = SanitizeInGameOOCMessage(message);

        var sendType = type;
        // If dead player LOOC is disabled, unless you are an admin with Moderator perms, send dead messages to dead chat
        if ((_adminManager.IsAdmin(player) && _adminManager.HasAdminFlag(player, AdminFlags.Moderator)) // Override if admin
            || _deadLoocEnabled
            || (!HasComp<GhostComponent>(source) && !_mobStateSystem.IsDead(source))) // Check that player is not dead
        {
        }
        else
            sendType = InGameOOCChatType.Dead;

        // If crit player LOOC is disabled, don't send the message at all.
        if (!_critLoocEnabled && _mobStateSystem.IsCritical(source))
            return;

        switch (sendType)
        {
            case InGameOOCChatType.Dead:
                SendDeadChat(source, player, message, hideChat);
                break;
            case InGameOOCChatType.Looc:
                SendLOOC(source, player, message, hideChat);
                break;
        }
    }

    #region Announcements

    /// <summary>
    /// Dispatches an announcement to all.
    /// </summary>
    /// <param name="message">The contents of the message</param>
    /// <param name="sender">The sender (Communications Console in Communications Console Announcement)</param>
    /// <param name="playSound">Play the announcement sound</param>
    /// <param name="colorOverride">Optional color for the announcement message</param>
    public void DispatchGlobalAnnouncement(
        string message,
        string? sender = null,
        bool playSound = true,
        SoundSpecifier? announcementSound = null,
        Color? colorOverride = null
        )
    {
        sender ??= Loc.GetString("chat-manager-sender-announcement");

        var wrappedMessage = Loc.GetString("chat-manager-sender-announcement-wrap-message", ("sender", sender), ("message", FormattedMessage.EscapeText(message)));
        _chatManager.ChatMessageToAll(ChatChannel.Radio, message, wrappedMessage, default, false, true, colorOverride);
        if (playSound)
        {
            _audio.PlayGlobal(announcementSound ?? DefaultAnnouncementSound, Filter.Broadcast(), true, AudioParams.Default.WithVolume(-2f));
        }
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Global station announcement from {sender}: {message}");
    }

    /// <summary>
    /// Dispatches an announcement to players selected by filter.
    /// </summary>
    /// <param name="filter">Filter to select players who will recieve the announcement</param>
    /// <param name="message">The contents of the message</param>
    /// <param name="source">The entity making the announcement (used to determine the station)</param>
    /// <param name="sender">The sender (Communications Console in Communications Console Announcement)</param>
    /// <param name="playDefaultSound">Play the announcement sound</param>
    /// <param name="announcementSound">Sound to play</param>
    /// <param name="colorOverride">Optional color for the announcement message</param>
    public void DispatchFilteredAnnouncement(
        Filter filter,
        string message,
        EntityUid? source = null,
        string? sender = null,
        bool playSound = true,
        SoundSpecifier? announcementSound = null,
        Color? colorOverride = null)
    {
        sender ??= Loc.GetString("chat-manager-sender-announcement");

        var wrappedMessage = Loc.GetString("chat-manager-sender-announcement-wrap-message", ("sender", sender), ("message", FormattedMessage.EscapeText(message)));
        _chatManager.ChatMessageToManyFiltered(filter, ChatChannel.Radio, message, wrappedMessage, source ?? default, false, true, colorOverride);
        if (playSound)
        {
            _audio.PlayGlobal(announcementSound ?? DefaultAnnouncementSound, filter, true, AudioParams.Default.WithVolume(-2f));
        }
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Station Announcement from {sender}: {message}");
    }

    /// <summary>
    /// Dispatches an announcement on a specific station
    /// </summary>
    /// <param name="source">The entity making the announcement (used to determine the station)</param>
    /// <param name="message">The contents of the message</param>
    /// <param name="sender">The sender (Communications Console in Communications Console Announcement)</param>
    /// <param name="playDefaultSound">Play the announcement sound</param>
    /// <param name="colorOverride">Optional color for the announcement message</param>
    public void DispatchStationAnnouncement(
        EntityUid source,
        string message,
        string? sender = null,
        bool playDefaultSound = true,
        SoundSpecifier? announcementSound = null,
        Color? colorOverride = null)
    {
        sender ??= Loc.GetString("chat-manager-sender-announcement");

        var wrappedMessage = Loc.GetString("chat-manager-sender-announcement-wrap-message", ("sender", sender), ("message", FormattedMessage.EscapeText(message)));
        var station = _stationSystem.GetOwningStation(source);

        if (station == null)
        {
            // you can't make a station announcement without a station
            return;
        }

        if (!TryComp<StationDataComponent>(station, out var stationDataComp)) return;

        var filter = _stationSystem.GetInStation(stationDataComp);

        _chatManager.ChatMessageToManyFiltered(filter, ChatChannel.Radio, message, wrappedMessage, source, false, true, colorOverride);

        if (playDefaultSound)
        {
            _audio.PlayGlobal(announcementSound ?? DefaultAnnouncementSound, filter, true, AudioParams.Default.WithVolume(-2f));
        }

        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Station Announcement on {station} from {sender}: {message}");
    }

    #endregion

    #region Private API

    private void SendEntitySpeak(
        EntityUid source,
        string originalMessage,
        ChatTransmitRange range,
        string? nameOverride,
        bool hideLog = false,
        bool ignoreActionBlocker = false
        )
    {
        // Radiant Sector: this is the final ordinary-speech path. Keep the empathy
        // interception here as well so +э can never be emitted as nearby speech.
        if (TrySendShadowkinEmpathy(source, originalMessage, nameOverride))
            return;

        if (!_actionBlocker.CanSpeak(source) && !ignoreActionBlocker)
            return;

        var message = TransformSpeech(source, originalMessage);

        if (message.Length == 0)
            return;

        // Radiant Sector: native speech is selected through the top-panel language menu.
        // "##" remains a one-message native-language override.
        var nativeLanguage = TryGetNativeLanguage(source);
        var requestedNativeLanguage = TryStripNativeLanguagePrefix(ref message);
        var canUseNativeLanguage = nativeLanguage != null && !HasComp<NativeLanguageUnfamiliarComponent>(source);
        var canUseGalactic = !HasComp<NativeLanguageOnlyComponent>(source);

        if (requestedNativeLanguage && !canUseNativeLanguage)
        {
            _popup.PopupEntity("Вы не знаете родной язык своего вида.", source, source);
            return;
        }

        var speaksNativeLanguage = canUseNativeLanguage
            && (HasComp<NativeLanguageOnlyComponent>(source) || _nativeLanguageSelected.Contains(source) || requestedNativeLanguage);

        if (!speaksNativeLanguage && !canUseGalactic)
        {
            _popup.PopupEntity("Вы не знаете общегалактический язык.", source, source);
            return;
        }

        var speech = GetSpeechVerb(source, message);

        // get the entity's apparent name (if no override provided).
        string name;
        if (nameOverride != null)
        {
            name = nameOverride;
        }
        else
        {
            var nameEv = new TransformSpeakerNameEvent(source, Name(source));
            RaiseLocalEvent(source, nameEv);
            name = nameEv.VoiceName;
            // Check for a speech verb override
            if (nameEv.SpeechVerb != null && _prototypeManager.TryIndex(nameEv.SpeechVerb, out var proto))
                speech = proto;
        }

        name = FormattedMessage.EscapeText(name);

        var wrappedMessage = Loc.GetString(speech.Bold ? "chat-manager-entity-say-bold-wrap-message" : "chat-manager-entity-say-wrap-message",
            ("entityName", name),
            ("verb", Loc.GetString(_random.Pick(speech.SpeechVerbStrings))),
            ("fontType", speech.FontId),
            ("fontSize", speech.FontSize),
            ("message", FormattedMessage.EscapeText(message)));

        if (speaksNativeLanguage)
            SendNativeLanguageInVoiceRange(nativeLanguage!, message, wrappedMessage, source, range);
        else
            SendGalacticLanguageInVoiceRange(message, wrappedMessage, source, range);

        var ev = new EntitySpokeEvent(source, message, null, null, speaksNativeLanguage ? nativeLanguage : null);
        RaiseLocalEvent(source, ev, true);

        // To avoid logging any messages sent by entities that are not players, like vendors, cloning, etc.
        // Also doesn't log if hideLog is true.
        if (!HasComp<ActorComponent>(source) || hideLog)
            return;

        if (originalMessage == message)
        {
            if (name != Name(source))
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Say from {ToPrettyString(source):user} as {name}: {originalMessage}.");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Say from {ToPrettyString(source):user}: {originalMessage}.");
        }
        else
        {
            if (name != Name(source))
                _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Say from {ToPrettyString(source):user} as {name}, original: {originalMessage}, transformed: {message}.");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Say from {ToPrettyString(source):user}, original: {originalMessage}, transformed: {message}.");
        }
    }

    private void SendEntityWhisper(
        EntityUid source,
        string originalMessage,
        ChatTransmitRange range,
        RadioChannelPrototype? channel,
        string? nameOverride,
        bool hideLog = false,
        bool ignoreActionBlocker = false
        )
    {
        if (!_actionBlocker.CanSpeak(source) && !ignoreActionBlocker)
            return;

        var message = TransformSpeech(source, FormattedMessage.RemoveMarkupOrThrow(originalMessage));
        if (message.Length == 0)
            return;

        // Radiant Sector: language knowledge restrictions apply to whispers as well.
        var nativeLanguage = TryGetNativeLanguage(source);
        var requestedNativeLanguage = TryStripNativeLanguagePrefix(ref message);
        var canUseNativeLanguage = nativeLanguage != null && !HasComp<NativeLanguageUnfamiliarComponent>(source);
        var canUseGalactic = !HasComp<NativeLanguageOnlyComponent>(source);

        if (requestedNativeLanguage && !canUseNativeLanguage)
        {
            _popup.PopupEntity("Вы не знаете родной язык своего вида.", source, source);
            return;
        }

        var speaksNativeLanguage = canUseNativeLanguage
            && (HasComp<NativeLanguageOnlyComponent>(source) || _nativeLanguageSelected.Contains(source) || requestedNativeLanguage);

        if (!speaksNativeLanguage && !canUseGalactic)
        {
            _popup.PopupEntity("Вы не знаете общегалактический язык.", source, source);
            return;
        }
        var obfuscatedMessage = ObfuscateMessageReadability(message, 0.2f);
        var spokenLanguage = speaksNativeLanguage ? nativeLanguage! : "Общегалактический";
        var languageObfuscatedMessage = ObfuscateNativeLanguage(spokenLanguage, message);

        // get the entity's name by visual identity (if no override provided).
        string nameIdentity = FormattedMessage.EscapeText(nameOverride ?? Identity.Name(source, EntityManager));
        // get the entity's name by voice (if no override provided).
        string name;
        if (nameOverride != null)
        {
            name = nameOverride;
        }
        else
        {
            var nameEv = new TransformSpeakerNameEvent(source, Name(source));
            RaiseLocalEvent(source, nameEv);
            name = nameEv.VoiceName;
        }
        name = FormattedMessage.EscapeText(name);

        var wrappedMessage = Loc.GetString("chat-manager-entity-whisper-wrap-message",
            ("entityName", name), ("message", FormattedMessage.EscapeText(message)));

        var wrappedobfuscatedMessage = Loc.GetString("chat-manager-entity-whisper-wrap-message",
            ("entityName", nameIdentity), ("message", FormattedMessage.EscapeText(obfuscatedMessage)));

        var wrappedUnknownMessage = Loc.GetString("chat-manager-entity-whisper-unknown-wrap-message",
            ("message", FormattedMessage.EscapeText(obfuscatedMessage)));

        var wrappedLanguageMessage = Loc.GetString("chat-manager-entity-whisper-wrap-message",
            ("entityName", nameIdentity), ("message", FormattedMessage.EscapeText(languageObfuscatedMessage)));

        if (speaksNativeLanguage)
        {
            // Radiant Sector: native whispers use the same language colour as ordinary speech.
            wrappedMessage = ApplyLanguageColor(nativeLanguage!, wrappedMessage, FormattedMessage.EscapeText(message));
            wrappedobfuscatedMessage = ApplyLanguageColor(nativeLanguage!, wrappedobfuscatedMessage, FormattedMessage.EscapeText(obfuscatedMessage));
            wrappedUnknownMessage = ApplyLanguageColor(nativeLanguage!, wrappedUnknownMessage, FormattedMessage.EscapeText(obfuscatedMessage));
            wrappedLanguageMessage = ApplyLanguageColor(nativeLanguage!, wrappedLanguageMessage, FormattedMessage.EscapeText(languageObfuscatedMessage));
        }


        foreach (var (session, data) in GetRecipients(source, WhisperMuffledRange))
        {
            EntityUid listener;

            if (session.AttachedEntity is not { Valid: true } playerEntity)
                continue;
            listener = session.AttachedEntity.Value;

            if (MessageRangeCheck(session, data, range) != MessageRangeCheckResult.Full)
                continue; // Won't get logged to chat, and ghosts are too far away to see the pop-up, so we just won't send it to them.

            var understandsLanguage = data.Observer || UnderstandsLanguage(listener, speaksNativeLanguage ? nativeLanguage : null);

            if (!understandsLanguage)
                _chatManager.ChatMessageToOne(ChatChannel.Whisper, languageObfuscatedMessage, wrappedLanguageMessage, source, false, session.Channel);
            else if (data.Range <= WhisperClearRange || data.Observer)
                _chatManager.ChatMessageToOne(ChatChannel.Whisper, message, wrappedMessage, source, false, session.Channel);

            //If listener is too far, they only hear fragments of the message
            else if (_examineSystem.InRangeUnOccluded(source, listener, WhisperMuffledRange))
                _chatManager.ChatMessageToOne(ChatChannel.Whisper, obfuscatedMessage, wrappedobfuscatedMessage, source, false, session.Channel);
            //If listener is too far and has no line of sight, they can't identify the whisperer's identity
            else
                _chatManager.ChatMessageToOne(ChatChannel.Whisper, obfuscatedMessage, wrappedUnknownMessage, source, false, session.Channel);
        }

        _replay.RecordServerMessage(new ChatMessage(ChatChannel.Whisper, message, wrappedMessage, GetNetEntity(source), null, MessageRangeHideChatForReplay(range)));

        var ev = new EntitySpokeEvent(source, message, channel, obfuscatedMessage, speaksNativeLanguage ? nativeLanguage : null);
        RaiseLocalEvent(source, ev, true);
        if (!hideLog)
            if (originalMessage == message)
            {
                if (name != Name(source))
                    _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Whisper from {ToPrettyString(source):user} as {name}: {originalMessage}.");
                else
                    _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Whisper from {ToPrettyString(source):user}: {originalMessage}.");
            }
            else
            {
                if (name != Name(source))
                    _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Whisper from {ToPrettyString(source):user} as {name}, original: {originalMessage}, transformed: {message}.");
                else
                    _adminLogger.Add(LogType.Chat, LogImpact.Low,
                    $"Whisper from {ToPrettyString(source):user}, original: {originalMessage}, transformed: {message}.");
            }
    }

    private void SendEntityEmote(
        EntityUid source,
        string action,
        ChatTransmitRange range,
        string? nameOverride,
        bool hideLog = false,
        bool checkEmote = true,
        bool ignoreActionBlocker = false,
        NetUserId? author = null
        )
    {
        if (!_actionBlocker.CanEmote(source) && !ignoreActionBlocker)
            return;

        // get the entity's apparent name (if no override provided).
        var ent = Identity.Entity(source, EntityManager);
        string name = FormattedMessage.EscapeText(nameOverride ?? Name(ent));

        // Emotes use Identity.Name, since it doesn't actually involve your voice at all.
        var wrappedMessage = Loc.GetString("chat-manager-entity-me-wrap-message",
            ("entityName", name),
            ("entity", ent),
            ("message", FormattedMessage.RemoveMarkupOrThrow(action)));

        bool emoteEventInvoked = false; // Frontier: track emote event
        if (checkEmote &&
            !TryEmoteChatInput(source, action, out emoteEventInvoked)) // Frontier: track emote event
        {
            return;
        }

        // Frontier: send custom emotes through custom event
        if (!emoteEventInvoked)
        {
            var ev = new NFEntityEmotedEvent(action);
            RaiseLocalEvent(source, ev, true);
        }
        // End Frontier

        SendInVoiceRange(ChatChannel.Emotes, action, wrappedMessage, source, range, author);
        if (!hideLog)
            if (name != Name(source))
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Emote from {ToPrettyString(source):user} as {name}: {action}");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Emote from {ToPrettyString(source):user}: {action}");
    }

    // ReSharper disable once InconsistentNaming
    private void SendLOOC(EntityUid source, ICommonSession player, string message, bool hideChat)
    {
        var name = FormattedMessage.EscapeText(Identity.Name(source, EntityManager));

        if (_adminManager.IsAdmin(player))
        {
            if (!_adminLoocEnabled) return;
        }
        else if (!_loocEnabled) return;

        // If crit player LOOC is disabled, don't send the message at all.
        if (!_critLoocEnabled && _mobStateSystem.IsCritical(source))
            return;

        var wrappedMessage = Loc.GetString("chat-manager-entity-looc-wrap-message",
            ("entityName", name),
            ("message", FormattedMessage.EscapeText(message)));

        SendInVoiceRange(ChatChannel.LOOC, message, wrappedMessage, source, hideChat ? ChatTransmitRange.HideChat : ChatTransmitRange.Normal, player.UserId);
        _adminLogger.Add(LogType.Chat, LogImpact.Low, $"LOOC from {player:Player}: {message}");
    }

    private void SendDeadChat(EntityUid source, ICommonSession player, string message, bool hideChat)
    {
        var clients = GetDeadChatClients();
        var playerName = Name(source);
        string wrappedMessage;
        if (_adminManager.IsAdmin(player))
        {
            wrappedMessage = Loc.GetString("chat-manager-send-admin-dead-chat-wrap-message",
                ("adminChannelName", Loc.GetString("chat-manager-admin-channel-name")),
                ("userName", player.Channel.UserName),
                ("message", FormattedMessage.EscapeText(message)));
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Admin dead chat from {player:Player}: {message}");
        }
        else
        {
            wrappedMessage = Loc.GetString("chat-manager-send-dead-chat-wrap-message",
                ("deadChannelName", Loc.GetString("chat-manager-dead-channel-name")),
                ("playerName", (playerName)),
                ("message", FormattedMessage.EscapeText(message)));
            _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Dead chat from {player:Player}: {message}");
        }

        _chatManager.ChatMessageToMany(ChatChannel.Dead, message, wrappedMessage, source, hideChat, true, clients.ToList(), author: player.UserId);
    }
    #endregion

    #region Utility

    private enum MessageRangeCheckResult
    {
        Disallowed,
        HideChat,
        Full
    }

    /// <summary>
    ///     If hideChat should be set as far as replays are concerned.
    /// </summary>
    private bool MessageRangeHideChatForReplay(ChatTransmitRange range)
    {
        return range == ChatTransmitRange.HideChat;
    }

    /// <summary>
    ///     Checks if a target as returned from GetRecipients should receive the message.
    ///     Keep in mind data.Range is -1 for out of range observers.
    /// </summary>
    private MessageRangeCheckResult MessageRangeCheck(ICommonSession session, ICChatRecipientData data, ChatTransmitRange range)
    {
        var initialResult = MessageRangeCheckResult.Full;
        switch (range)
        {
            case ChatTransmitRange.Normal:
                initialResult = MessageRangeCheckResult.Full;
                break;
            case ChatTransmitRange.GhostRangeLimit:
                initialResult = (data.Observer && data.Range < 0 && !_adminManager.IsAdmin(session)) ? MessageRangeCheckResult.HideChat : MessageRangeCheckResult.Full;
                break;
            case ChatTransmitRange.HideChat:
                initialResult = MessageRangeCheckResult.HideChat;
                break;
            case ChatTransmitRange.NoGhosts:
                initialResult = (data.Observer && !_adminManager.IsAdmin(session)) ? MessageRangeCheckResult.Disallowed : MessageRangeCheckResult.Full;
                break;
            // Frontier - prevent TVs from spamming the poor, poor admins
            case ChatTransmitRange.GhostRangeLimitNoAdminCheck:
                initialResult = (data.Observer && data.Range < 0) ? MessageRangeCheckResult.HideChat : MessageRangeCheckResult.Full;
                break;
                // End Frontier
        }
        var insistHideChat = data.HideChatOverride ?? false;
        var insistNoHideChat = !(data.HideChatOverride ?? true);
        if (insistHideChat && initialResult == MessageRangeCheckResult.Full)
            return MessageRangeCheckResult.HideChat;
        if (insistNoHideChat && initialResult == MessageRangeCheckResult.HideChat)
            return MessageRangeCheckResult.Full;
        return initialResult;
    }

    /// <summary>
    ///     Sends a chat message to the given players in range of the source entity.
    /// </summary>
    private void SendInVoiceRange(ChatChannel channel, string message, string wrappedMessage, EntityUid source, ChatTransmitRange range, NetUserId? author = null)
    {
        foreach (var (session, data) in GetRecipients(source, VoiceRange))
        {
            var entRange = MessageRangeCheck(session, data, range);
            if (entRange == MessageRangeCheckResult.Disallowed)
                continue;
            var entHideChat = entRange == MessageRangeCheckResult.HideChat;
            _chatManager.ChatMessageToOne(channel, message, wrappedMessage, source, entHideChat, session.Channel, author: author);
        }

        _replay.RecordServerMessage(new ChatMessage(channel, message, wrappedMessage, GetNetEntity(source), null, MessageRangeHideChatForReplay(range)));
    }

    /// <summary>
    /// Radiant Sector: relays a radio speaker into nearby chat without losing the language
    /// spoken by the original sender. Handheld radios use this path because their speaker is
    /// a separate entity rather than the listener's headset.
    /// </summary>
    public void SendRadioRelayInVoiceRange(
        EntityUid radioSpeaker,
        string message,
        InGameICChatType outputType,
        string speakerName,
        string? language)
    {
        var channel = outputType == InGameICChatType.Whisper ? ChatChannel.Whisper : ChatChannel.Local;
        var wrapper = outputType == InGameICChatType.Whisper
            ? "chat-manager-entity-whisper-wrap-message"
            : "chat-manager-entity-say-wrap-message";
        var speech = GetSpeechVerb(radioSpeaker, message);
        var wrapped = outputType == InGameICChatType.Whisper
            ? Loc.GetString(wrapper, ("entityName", speakerName), ("message", FormattedMessage.EscapeText(message)))
            : Loc.GetString(wrapper,
                ("entityName", speakerName),
                ("verb", Loc.GetString(_random.Pick(speech.SpeechVerbStrings))),
                ("fontType", speech.FontId),
                ("fontSize", speech.FontSize),
                ("message", FormattedMessage.EscapeText(message)));

        var obfuscated = language == null
            ? ObfuscateNativeLanguage("Общегалактический", message)
            : ObfuscateNativeLanguage(language, message);
        var escapedMessage = FormattedMessage.EscapeText(message);
        var escapedObfuscated = FormattedMessage.EscapeText(obfuscated);
        var readableWrapped = language == null ? wrapped : ApplyLanguageColor(language, wrapped, escapedMessage);
        var garbledWrapped = wrapped.Replace(escapedMessage, escapedObfuscated);
        if (language != null)
            garbledWrapped = ApplyLanguageColor(language, garbledWrapped, escapedObfuscated);

        foreach (var (session, data) in GetRecipients(radioSpeaker, VoiceRange))
        {
            var entRange = MessageRangeCheck(session, data, ChatTransmitRange.GhostRangeLimitNoAdminCheck);
            if (entRange == MessageRangeCheckResult.Disallowed)
                continue;

            var understands = data.Observer;
            if (!understands && session.AttachedEntity is { Valid: true } listener)
            {
                understands = UnderstandsLanguage(listener, language);
            }

            _chatManager.ChatMessageToOne(
                channel,
                understands ? message : obfuscated,
                understands ? readableWrapped : garbledWrapped,
                radioSpeaker,
                entRange == MessageRangeCheckResult.HideChat,
                session.Channel);
        }
    }

    /// <summary>
    /// Radiant Sector: sends Galactic Common. Native-only characters receive it as unreadable speech.
    /// </summary>
    private void SendGalacticLanguageInVoiceRange(string message, string wrappedMessage, EntityUid source, ChatTransmitRange range)
    {
        var obfuscated = ObfuscateNativeLanguage("Общегалактический", message);
        var escapedMessage = FormattedMessage.EscapeText(message);
        var escapedObfuscated = FormattedMessage.EscapeText(obfuscated);
        var wrappedObfuscated = wrappedMessage.Replace(escapedMessage, escapedObfuscated);

        foreach (var (session, data) in GetRecipients(source, VoiceRange))
        {
            var entRange = MessageRangeCheck(session, data, range);
            if (entRange == MessageRangeCheckResult.Disallowed)
                continue;

            var understands = data.Observer;
            if (!understands && session.AttachedEntity is { Valid: true } listener)
                understands = UnderstandsLanguage(listener, null);
            _chatManager.ChatMessageToOne(
                ChatChannel.Local,
                understands ? message : obfuscated,
                understands ? wrappedMessage : wrappedObfuscated,
                source,
                entRange == MessageRangeCheckResult.HideChat,
                session.Channel);
        }

        _replay.RecordServerMessage(new ChatMessage(ChatChannel.Local, message, wrappedMessage, GetNetEntity(source), null, MessageRangeHideChatForReplay(range)));
    }

    /// <summary>
    /// Radiant Sector: sends native racial speech as readable text only to characters of the same language.
    /// </summary>
    private void SendNativeLanguageInVoiceRange(string language, string message, string wrappedMessage, EntityUid source, ChatTransmitRange range)
    {
        var obfuscated = ObfuscateNativeLanguage(language, message);
        var escapedMessage = FormattedMessage.EscapeText(message);
        var wrappedReadable = ApplyLanguageColor(language, wrappedMessage, escapedMessage);
        var escapedObfuscated = FormattedMessage.EscapeText(obfuscated);
        var wrappedObfuscated = ApplyLanguageColor(language, wrappedMessage.Replace(escapedMessage, escapedObfuscated), escapedObfuscated);

        foreach (var (session, data) in GetRecipients(source, VoiceRange))
        {
            var entRange = MessageRangeCheck(session, data, range);
            if (entRange == MessageRangeCheckResult.Disallowed)
                continue;

            var understands = data.Observer;
            if (!understands && session.AttachedEntity is { Valid: true } listener)
                understands = UnderstandsLanguage(listener, language);
            _chatManager.ChatMessageToOne(
                ChatChannel.Local,
                understands ? message : obfuscated,
                understands ? wrappedReadable : wrappedObfuscated,
                source,
                entRange == MessageRangeCheckResult.HideChat,
                session.Channel);
        }

        _replay.RecordServerMessage(new ChatMessage(ChatChannel.Local, message, wrappedMessage, GetNetEntity(source), null, MessageRangeHideChatForReplay(range)));
    }

    /// <summary>
    /// Radiant Sector: returns the native spoken language for playable humanoid species.
    /// </summary>
    internal string? TryGetNativeLanguage(EntityUid entity)
    {
        return SpeciesLanguageUtility.GetNativeLanguage(EntityManager, entity);
    }

    /// <summary>
    /// Radiant Sector: checks whether a listener understands a spoken language. Borgs understand every language.
    /// </summary>
    private bool UnderstandsLanguage(EntityUid listener, string? language)
    {
        if (TryGetNativeLanguage(listener) == "Двоичный")
            return true;

        return language == null
            ? !HasComp<NativeLanguageOnlyComponent>(listener)
            : TryGetNativeLanguage(listener) == language && !HasComp<NativeLanguageUnfamiliarComponent>(listener);
    }

    /// <summary>
    /// Radiant Sector: creates the radio line appropriate for one listener's language knowledge.
    /// </summary>
    internal MsgChatMessage GetRadioMessageForListener(MsgChatMessage radioMessage, EntityUid listener, string? language)
    {
        // Radiant Sector: ghosts observe the original radio line; language filtering is only for living listeners.
        if (HasComp<GhostComponent>(listener))
            return radioMessage;

        // Radiant Sector: a missing language means Galactic Common. Native-only characters must
        // receive it garbled even over radio, just like they do in local speech.
        var understands = UnderstandsLanguage(listener, language);

        if (language == null && understands)
            return radioMessage;

        var original = radioMessage.Message;
        var visibleLanguage = language ?? "Общегалактический";
        var visibleMessage = understands ? original.Message : ObfuscateNativeLanguage(visibleLanguage, original.Message);
        var escapedOriginal = FormattedMessage.EscapeText(original.Message);
        var escapedVisible = FormattedMessage.EscapeText(visibleMessage);
        var wrapped = original.WrappedMessage.Replace(escapedOriginal, escapedVisible);
        if (language != null)
            wrapped = ApplyLanguageColor(language, wrapped, escapedVisible);

        return new MsgChatMessage
        {
            Message = new ChatMessage(
                original.Channel,
                visibleMessage,
                wrapped,
                original.SenderEntity,
                original.SenderKey,
                original.HideChat,
                original.MessageColorOverride,
                original.AudioPath,
                original.AudioVolume),
        };
    }

    /// <summary>
    /// Radiant Sector: opens the language window from the top HUD button.
    /// </summary>
    private void OnLanguageMenuRequest(LanguageMenuRequestEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } speaker)
            return;

        SendLanguageMenuState(speaker, args.SenderSession);
    }

    /// <summary>
    /// Radiant Sector: validates the player's language selection and then refreshes the window state.
    /// </summary>
    private void OnLanguageMenuSelect(LanguageMenuSelectEvent ev, EntitySessionEventArgs args)
    {
        if (args.SenderSession.AttachedEntity is not { Valid: true } speaker || TryGetNativeLanguage(speaker) == null)
            return;

        SetNativeLanguageSelected(speaker, speaker, ev.Native);
        SendLanguageMenuState(speaker, args.SenderSession);
    }

    /// <summary>
    /// Radiant Sector: sends the single native language available to this character to its client.
    /// </summary>
    private void SendLanguageMenuState(EntityUid speaker, ICommonSession recipient)
    {
        if (TryGetNativeLanguage(speaker) is not { } nativeLanguage)
            return;

        var canUseNative = !HasComp<NativeLanguageUnfamiliarComponent>(speaker);
        var canUseGalactic = !HasComp<NativeLanguageOnlyComponent>(speaker);
        var nativeSelected = canUseNative && (!canUseGalactic || _nativeLanguageSelected.Contains(speaker));
        RaiseNetworkEvent(new LanguageMenuStateEvent(nativeLanguage, nativeSelected, canUseNative, canUseGalactic), recipient);
    }

    /// <summary>
    /// Radiant Sector: changes the speech language and confirms it privately to the character.
    /// </summary>
    private void SetNativeLanguageSelected(EntityUid speaker, EntityUid user, bool native)
    {
        if (speaker != user || TryGetNativeLanguage(speaker) is not { } nativeLanguage)
            return;

        if (native && HasComp<NativeLanguageUnfamiliarComponent>(speaker))
            return;

        if (!native && HasComp<NativeLanguageOnlyComponent>(speaker))
            return;

        if (native)
            _nativeLanguageSelected.Add(speaker);
        else
            _nativeLanguageSelected.Remove(speaker);

        var selected = native ? nativeLanguage : "Общегалактический";
        _popup.PopupEntity($"Выбран язык: {selected}", speaker, user);
    }

    /// <summary>
    /// Radiant Sector: removes the language marker. Example: "## привет".
    /// </summary>
    private static bool TryStripNativeLanguagePrefix(ref string message)
    {
        if (!message.StartsWith("##"))
            return false;

        message = message[2..].TrimStart();
        return message.Length > 0;
    }

    /// <summary>
    /// Radiant Sector: preserves word count while making an unknown language unreadable.
    /// </summary>
    internal static string ObfuscateNativeLanguage(string language, string message)
    {
        // Radiant Sector: each language has a distinct pool so unreadable speech still sounds recognisably alien.
        string[] fragments = language switch
        {
            "Общегалактический" => ["эм", "ах", "тс", "мм"],
            "Канилунц" => ["раур", "веф", "шай", "фур", "айо", "вис", "эрель", "линц", "касари", "тайвас", "эйик", "аррей"], // Radiant Sector: adapted from Vulpkanin naming and language references.
            "Сиик'тайр" => ["мяу", "мяк", "мя", "миу", "ау", "мяв", "мрау", "миау", "мурррр", "ххссс", "мяф"], // Radiant Sector: Tajaran howls and meows.
            "Счечи" => ["чи", "ри", "ше", "ти", "ка", "ра", "ма", "са", "на", "та", "ла", "ши", "счи", "крр", "трек", "пии"], // Radiant Sector: Resomi croaks, cracks and squeaks.
            "Синта'Унати" => ["ссссс", "щщщщ", "сщщ", "сщщщ", "щщщхх", "хщщ", "ххххх", "щхххх", "схххх", "счхххх", "чхххсхч"], // Radiant Sector: Unathi hissing and rattling speech.
            "Вокс-пиджин" => ["кри", "чир", "тви", "скра", "крак", "врии", "трак", "кшш", "скав", "грак", "кхр", "тш"], // Radiant Sector: adapted from Vox Pidgin descriptions.
            "Корневой язык" => ["пффпффпуфпуфпффпфф", "пфф", "пуф", "пффффф", "пфффпуф", "пфффффпуфпуфпуфпффффф", "пффпуф", "пуфпуф", "пффпифпафпфпуф"], // Radiant Sector: Diona root-language voice chords.
            "Бабблилиш" => ["блюмп", "блюф", "бульк", "баблпаф", "бламп", "бабл", "блимпаф", "блааамп", "блабл-бламп"], // Radiant Sector: Slimeperson bubbling speech.
            "Щёлкающий" => ["цк", "тцк", "шш", "крр", "клак", "тк", "цирр", "клик", "щелк", "скр", "трр", "кш"], // Radiant Sector: Arachnid click-and-hiss speech.
            "Моффик" => ["sekygglitomånkönvii", "detdetdår", "møtmå", "ån", "gårköndagint", "viitehjaomköntyclaviinæbraånhönledetygglithankäytokmo", "sek"], // Radiant Sector: intentionally difficult Nian speech.
            "Нехина" => ["бульк", "гррл", "хаар", "шарк", "глур", "рррак", "трал", "фирр", "карр", "брул", "хра", "гарр"], // Radiant Sector: Feroxi aquatic speech.
            "Сумеречный" => ["мрр", "вум", "ши", "нх", "сум", "тень", "вэй", "лум", "шор", "мрак", "эха", "нур"], // Radiant Sector: Shadowkin twilight speech.
            "Кхаздар" => ["грум", "кхаз", "дор", "бар", "кам", "рун", "молот", "сталь", "горн", "брон", "грим", "двар"], // Radiant Sector: Dwarven craft language.
            "Кансэй" => ["ка", "сэй", "но", "раи", "они", "дзэн", "кай", "мори", "хира", "сора", "такэ", "юми"], // Radiant Sector: Japanese-Oni contact language.
            "Аэрийский" => ["три", "лии", "кьи", "фью", "чир", "сви", "айра", "крыл", "пев", "вью", "рии", "лаи"], // Radiant Sector: Harpy song-like speech.
            "Крикли" => ["зик", "кек", "трик", "грак", "рык", "тыгдык", "варилмбик", "лек", "мик", "лык", "сик", "лысын", "тирмик", "хыхык", "тикмик"], // Radiant Sector: goblin speech pool.
            "Шелар" => ["араваар", "сингмасир", "налливаддд", "неввас", "галллипер", "имабил", "забимммил", "увввалим", "нафффис", "хеливвван"], // Radiant Sector: Sheleg speech pool.
            "Арканийский" => ["авациа", "егул", "ар", "гаисиуваи", "оумико", "эледон", "ли", "асхииди", "вэ", "декипаа", "опрес", "аздил"], // Radiant Sector: Arcanian speech pool.
            "НекоМетрический" => ["ня", "каничива", "нья", "кия", "некочуу", "отто", "нянмунядесунуняича", "ухуху", "каваимунячуу", "китдесу", "ньябооп", "кьяа", "бооп", "со"], // Radiant Sector: intentionally broken Japanese-like Felinid speech.
            "Двоичный" => ["0101", "1010", "0011", "1100", "0110", "1001", "пик", "бип", "трр", "клик", "11010000", "404", "1P0", "rn0"],
            _ => ["а", "эм", "хм", "тс"],
        };

        var words = message.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        for (var index = 0; index < words.Length; index++)
        {
            var seed = index;
            foreach (var character in words[index])
                seed += character;

            var count = 1 + (seed % 3);
            var pieces = new string[count];
            for (var piece = 0; piece < count; piece++)
                pieces[piece] = fragments[(seed + piece) % fragments.Length];

            words[index] = string.Concat(pieces);
        }

        return string.Join(' ', words);
    }

    /// <summary>
    /// Radiant Sector: reproduces Goob-style coloured native speech while keeping Galactic Common white.
    /// </summary>
    internal static string ApplyLanguageColor(string language, string wrappedMessage, string escapedMessage) // Radiant Sector: also used by radio delivery.
    {
        var color = language switch
        {
            "Синта'Унати" => "#2ACA2A",
            "Вокс-пиджин" => "#A489A0",
            "Корневой язык" => "#A64E14",
            "Бабблилиш" => "#24D1B9",
            "Моффик" => "#C7DF2E",
            "Щёлкающий" => "#B0B0B0",
            "Канилунц" => "#D69B3D",
            "Сиик'тайр" => "#D6A36E",
            "Счечи" => "#4DBFD9",
            "Нехина" => "#5FAEE3",
            "Сумеречный" => "#C29EFF",
            "Кхаздар" => "#C78E42",
            "Кансэй" => "#D95B67",
            "Аэрийский" => "#8ED5E8",
            "Крикли" => "#A7C746",
            "Шелар" => "#B5B9DF",
            "Арканийский" => "#D888E8",
            "НекоМетрический" => "#E5A1C7", // Radiant Sector
            "Двоичный" => "#7FD8FF",
            _ => "#FFFFFF",
        };

        return wrappedMessage.Replace(escapedMessage, $"[color={color}]{escapedMessage}[/color]");
    }

    /// <summary>
    /// Sends an emote chat line only to the specified pair (source and target), ignoring normal voice range.
    /// Used for private ERP interactions that should not be visible to bystanders.
    /// </summary>
    public void SendPrivateEmotePair(EntityUid source, EntityUid target, string action, string? nameOverride = null, bool hideLog = false)
    {
        var ent = Identity.Entity(source, EntityManager);
        var name = FormattedMessage.EscapeText(nameOverride ?? Name(ent));

        var wrappedMessage = Loc.GetString("chat-manager-entity-me-wrap-message",
            ("entityName", name),
            ("entity", ent),
            ("message", FormattedMessage.RemoveMarkupOrThrow(action)));

        // Deliver only to source + target sessions.
        foreach (var session in _playerManager.Sessions)
        {
            var attached = session.AttachedEntity;
            if (attached != source && attached != target)
                continue;

            _chatManager.ChatMessageToOne(ChatChannel.Emotes, action, wrappedMessage, source, hideChat: false, session.Channel, author: session.UserId);
        }

        if (!hideLog)
        {
            if (name != Name(source))
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Private emote from {ToPrettyString(source):user} as {name}: {action}");
            else
                _adminLogger.Add(LogType.Chat, LogImpact.Low, $"Private emote from {ToPrettyString(source):user}: {action}");
        }
    }

    /// <summary>
    ///     Returns true if the given player is 'allowed' to send the given message, false otherwise.
    /// </summary>
    private bool CanSendInGame(string message, IConsoleShell? shell = null, ICommonSession? player = null)
    {
        // Non-players don't have to worry about these restrictions.
        if (player == null)
            return true;

        var mindContainerComponent = player.ContentData()?.Mind;

        if (mindContainerComponent == null)
        {
            shell?.WriteError("You don't have a mind!");
            return false;
        }

        if (player.AttachedEntity is not { Valid: true } _)
        {
            shell?.WriteError("You don't have an entity!");
            return false;
        }

        return !_chatManager.MessageCharacterLimit(player, message);
    }

    // ReSharper disable once InconsistentNaming
    private string SanitizeInGameICMessage(EntityUid source, string message, out string? emoteStr, bool capitalize = true, bool punctuate = false, bool capitalizeTheWordI = true)
    {
        var newMessage = SanitizeMessageReplaceWords(message.Trim());

        GetRadioKeycodePrefix(source, newMessage, out newMessage, out var prefix);

        // Sanitize it first as it might change the word order
        _sanitizer.TrySanitizeEmoteShorthands(newMessage, source, out newMessage, out emoteStr);

        if (capitalize)
            newMessage = SanitizeMessageCapital(newMessage);
        if (capitalizeTheWordI)
            newMessage = SanitizeMessageCapitalizeTheWordI(newMessage, "i");
        if (punctuate)
            newMessage = SanitizeMessagePeriod(newMessage);

        return prefix + newMessage;
    }

    private string SanitizeInGameOOCMessage(string message)
    {
        var newMessage = message.Trim();
        newMessage = FormattedMessage.EscapeText(newMessage);

        return newMessage;
    }

    public string TransformSpeech(EntityUid sender, string message)
    {
        var ev = new TransformSpeechEvent(sender, message);
        RaiseLocalEvent(ev);

        return ev.Message;
    }

    public bool CheckIgnoreSpeechBlocker(EntityUid sender, bool ignoreBlocker)
    {
        if (ignoreBlocker)
            return ignoreBlocker;

        var ev = new CheckIgnoreSpeechBlockerEvent(sender, ignoreBlocker);
        RaiseLocalEvent(sender, ev, true);

        return ev.IgnoreBlocker;
    }

    private IEnumerable<INetChannel> GetDeadChatClients()
    {
        return Filter.Empty()
            .AddWhereAttachedEntity(HasComp<GhostComponent>)
            .Recipients
            .Union(_adminManager.ActiveAdmins)
            .Select(p => p.Channel);
    }

    private string SanitizeMessagePeriod(string message)
    {
        if (string.IsNullOrEmpty(message))
            return message;
        // Adds a period if the last character is a letter.
        if (char.IsLetter(message[^1]))
            message += ".";
        return message;
    }

    public static readonly ProtoId<ReplacementAccentPrototype> ChatSanitize_Accent = "chatsanitize";

    public string SanitizeMessageReplaceWords(string message)
    {
        if (string.IsNullOrEmpty(message)) return message;

        var msg = message;

        msg = _wordreplacement.ApplyReplacements(msg, ChatSanitize_Accent);

        return msg;
    }

    /// <summary>
    ///     Returns list of players and ranges for all players withing some range. Also returns observers with a range of -1.
    /// </summary>
    private Dictionary<ICommonSession, ICChatRecipientData> GetRecipients(EntityUid source, float voiceGetRange)
    {
        // TODO proper speech occlusion

        var recipients = new Dictionary<ICommonSession, ICChatRecipientData>();
        var ghostHearing = GetEntityQuery<GhostHearingComponent>();
        var xforms = GetEntityQuery<TransformComponent>();

        var transformSource = xforms.GetComponent(source);
        var sourceMapId = transformSource.MapID;
        var sourceCoords = transformSource.Coordinates;

        foreach (var player in _playerManager.Sessions)
        {
            if (player.AttachedEntity is not { Valid: true } playerEntity)
                continue;

            var transformEntity = xforms.GetComponent(playerEntity);

            if (transformEntity.MapID != sourceMapId)
                continue;

            var observer = ghostHearing.HasComponent(playerEntity);

            // even if they are a ghost hearer, in some situations we still need the range
            if (sourceCoords.TryDistance(EntityManager, transformEntity.Coordinates, out var distance) && distance < voiceGetRange)
            {
                recipients.Add(player, new ICChatRecipientData(distance, observer));
                continue;
            }

            if (observer)
                recipients.Add(player, new ICChatRecipientData(-1, true));
        }

        RaiseLocalEvent(new ExpandICChatRecipientsEvent(source, voiceGetRange, recipients));
        return recipients;
    }

    public readonly record struct ICChatRecipientData(float Range, bool Observer, bool? HideChatOverride = null)
    {
    }

    private string ObfuscateMessageReadability(string message, float chance)
    {
        var modifiedMessage = new StringBuilder(message);

        for (var i = 0; i < message.Length; i++)
        {
            if (char.IsWhiteSpace((modifiedMessage[i])))
            {
                continue;
            }

            if (_random.Prob(1 - chance))
            {
                modifiedMessage[i] = '~';
            }
        }

        return modifiedMessage.ToString();
    }

    public string BuildGibberishString(IReadOnlyList<char> charOptions, int length)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < length; i++)
        {
            sb.Append(_random.Pick(charOptions));
        }
        return sb.ToString();
    }

    #endregion
}

/// <summary>
///     This event is raised before chat messages are sent out to clients. This enables some systems to send the chat
///     messages to otherwise out-of view entities (e.g. for multiple viewports from cameras).
/// </summary>
public record ExpandICChatRecipientsEvent(EntityUid Source, float VoiceRange, Dictionary<ICommonSession, ChatSystem.ICChatRecipientData> Recipients)
{
}

/// <summary>
///     Raised broadcast in order to transform speech.transmit
/// </summary>
public sealed class TransformSpeechEvent : EntityEventArgs
{
    public EntityUid Sender;
    public string Message;

    public TransformSpeechEvent(EntityUid sender, string message)
    {
        Sender = sender;
        Message = message;
    }
}

public sealed class CheckIgnoreSpeechBlockerEvent : EntityEventArgs
{
    public EntityUid Sender;
    public bool IgnoreBlocker;

    public CheckIgnoreSpeechBlockerEvent(EntityUid sender, bool ignoreBlocker)
    {
        Sender = sender;
        IgnoreBlocker = ignoreBlocker;
    }
}

/// <summary>
///     Raised on an entity when it speaks, either through 'say' or 'whisper'.
/// </summary>
public sealed class EntitySpokeEvent : EntityEventArgs
{
    public readonly EntityUid Source;
    public readonly string Message;
    public readonly string? ObfuscatedMessage; // not null if this was a whisper

    /// <summary>
    ///     Radiant Sector: native language selected by the speaker, if any.
    /// </summary>
    public readonly string? Language;

    /// <summary>
    ///     If the entity was trying to speak into a radio, this was the channel they were trying to access. If a radio
    ///     message gets sent on this channel, this should be set to null to prevent duplicate messages.
    /// </summary>
    public RadioChannelPrototype? Channel;

    public EntitySpokeEvent(EntityUid source, string message, RadioChannelPrototype? channel, string? obfuscatedMessage, string? language = null)
    {
        Source = source;
        Message = message;
        Channel = channel;
        ObfuscatedMessage = obfuscatedMessage;
        Language = language;
    }
}

/// <summary>
///     InGame IC chat is for chat that is specifically ingame (not lobby) but is also in character, i.e. speaking.
/// </summary>
// ReSharper disable once InconsistentNaming
public enum InGameICChatType : byte
{
    Speak,
    Emote,
    Whisper
}

/// <summary>
///     InGame OOC chat is for chat that is specifically ingame (not lobby) but is OOC, like deadchat or LOOC.
/// </summary>
public enum InGameOOCChatType : byte
{
    Looc,
    Dead
}

/// <summary>
///     Controls transmission of chat.
/// </summary>
public enum ChatTransmitRange : byte
{
    /// Acts normal, ghosts can hear across the map, etc.
    Normal,
    /// Normal but ghosts are still range-limited.
    GhostRangeLimit,
    /// Hidden from the chat window.
    HideChat,
    /// Ghosts can't hear or see it at all. Regular players can if in-range.
    NoGhosts,
    /// Frontier: Normal, ghosts are still range-limited, and won't spam admins
    GhostRangeLimitNoAdminCheck,
}

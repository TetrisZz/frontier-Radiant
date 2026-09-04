using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Cuffs.Components;
using Content.Shared._radiant;
using Content.Shared.DetailExaminable;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.ERP.Components;
using Content.Server.Chat.Systems;
using Content.Server._radiant.Arousal;
using Content.Server.Popups;
using Content.Shared._radiant.ERP;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server.Interaction.Panel
{
    public sealed class InteractionPanelSystem : EntitySystem
    {
        [Dependency] private readonly IPrototypeManager _prototypeManager = default!;
        [Dependency] private readonly IEntityManager _entManager = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly SharedAudioSystem _audio = default!;
        [Dependency] private readonly ChatSystem _chatSystem = default!;
        [Dependency] private readonly InventorySystem _inventorySystem = default!;
        [Dependency] private readonly SharedInteractionSystem _interaction = default!;
        [Dependency] private readonly SharedHandsSystem _hands = default!;
        [Dependency] private readonly ArousalSystem _arousal = default!;

        private readonly Dictionary<NetEntity, DateTime> _lastInteractionTimes = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeNetworkEvent<InteractionPressedEvent>(OnInteractionPressed);
        }

        private void OnInteractionPressed(InteractionPressedEvent ev)
        {
            HandleInteraction(ev.User, ev.Target, ev.InteractionId, ev.Prototype, ev.HideFromOthers, ev.ArousalHint);
        }

        public void HandleInteraction(NetEntity user, NetEntity? target, string interactionId, InteractionPrototype? prototype, bool hideFromOthers, int arousalHint = 0)
        {
            interactionId = interactionId.Trim();

            var userEntity = _entManager.GetEntity(user);
            if (HasComp<GhostComponent>(userEntity) && !HasComp<HumanoidAppearanceComponent>(userEntity))
                return;

            if (_entManager.TryGetComponent<MobThresholdsComponent>(userEntity, out var userThresholds) &&
                userThresholds.CurrentThresholdState != MobState.Alive &&
                userThresholds.CurrentThresholdState != MobState.Invalid)
                return;

            // Networked ev.Prototype is incomplete (e.g. Points default to 0). Prefer server prototype data when the id exists.
            var useImportedPlaceholderPath = false;
            InteractionPrototype interactionPrototype;
            if (_prototypeManager.TryIndex<InteractionPrototype>(interactionId, out var indexedPrototype))
            {
                interactionPrototype = indexedPrototype;
            }
            else if (prototype != null)
            {
                interactionPrototype = prototype;
                useImportedPlaceholderPath = true;
            }
            else
            {
                return;
            }

            if (interactionPrototype.Solo != (target == null))
                return;

            EntityUid? targetEntity = null;
            if (target != null)
            {
                targetEntity = _entManager.GetEntity(target.Value);

                if (IsErpDenied(targetEntity.Value))
                    return;

                if (_entManager.TryGetComponent<MobThresholdsComponent>(targetEntity.Value, out var targetThresholds) &&
                    targetThresholds.CurrentThresholdState != MobState.Alive &&
                    targetThresholds.CurrentThresholdState != MobState.Invalid)
                {
                    if (_entManager.TryGetComponent<ActorComponent>(userEntity, out var actor))
                    {
                        var message = Loc.GetString("interaction-target-not-alive-message");
                        _popupSystem.PopupEntity(message, userEntity, actor.PlayerSession, PopupType.Small);
                    }
                    return;
                }

                if (_entManager.TryGetComponent<TransformComponent>(targetEntity.Value, out var targetTransform))
                {
                    if (!_interaction.InRangeUnobstructed(userEntity, targetTransform.Coordinates, range: 2f,
                        collisionMask: CollisionGroup.Impassable, popup: false))
                    {
                        if (_entManager.TryGetComponent<ActorComponent>(userEntity, out var actor))
                        {
                            var message = Loc.GetString("interaction-target-unreachable-message");
                            _popupSystem.PopupEntity(message, userEntity, actor.PlayerSession, PopupType.Small);
                        }
                        return;
                    }
                }
            }

            if (IsErpDenied(userEntity))
                return;

            // Radiant sector: never trust only the client-side list. Surgery can
            // change ERP anatomy, so the server validates the actual organs too.
            if (!IsInteractionAnatomyAllowed(userEntity, targetEntity, interactionPrototype))
                return;

            var delayKey = target ?? user;
            if (_lastInteractionTimes.TryGetValue(delayKey, out var lastInteractionTime))
            {
                if (DateTime.UtcNow - lastInteractionTime < interactionPrototype.UseDelay && !useImportedPlaceholderPath)
                {
                    var message = Loc.GetString("interaction-delay-message");

                    if (_entManager.TryGetComponent<ActorComponent>(userEntity, out var actor))
                        _popupSystem.PopupEntity(message, userEntity, actor.PlayerSession, PopupType.Small);
                    return;
                }
                else if (DateTime.UtcNow - lastInteractionTime < TimeSpan.FromSeconds(2) && useImportedPlaceholderPath)
                {
                    var message = Loc.GetString("interaction-delay-message");

                    if (_entManager.TryGetComponent<ActorComponent>(userEntity, out var actor))
                        _popupSystem.PopupEntity(message, userEntity, actor.PlayerSession, PopupType.Small);
                    return;
                }
            }

            _lastInteractionTimes[delayKey] = DateTime.UtcNow;
            if (interactionPrototype.RequiredClothingSlots != null)
            {
                if (TryComp<InventoryComponent>(userEntity, out var inventory))
                {
                    foreach (var slot in interactionPrototype.RequiredClothingSlots)
                    {
                        if (_inventorySystem.TryGetSlotEntity(userEntity, slot, out _, inventory))
                        {
                            var message = Loc.GetString("interaction-hasclothing-message");
                            if (_entManager.TryGetComponent<ActorComponent>(userEntity, out var actor))
                                _popupSystem.PopupEntity(message, userEntity, actor.PlayerSession, PopupType.Small);
                            return;
                        }
                    }
                }

                if (targetEntity != null && TryComp<InventoryComponent>(targetEntity.Value, out var targetInventory))
                {
                    var requiredSlots = interactionPrototype.RequiredClothingSlots ?? Enumerable.Empty<string>();
                    var oneRequiredSlots = interactionPrototype.OneRequiredClothingSlots ?? Enumerable.Empty<string>();

                    var allSlots = requiredSlots.Concat(oneRequiredSlots);

                    foreach (var slot in allSlots)
                    {
                        if (_inventorySystem.TryGetSlotEntity(targetEntity.Value, slot, out _, targetInventory))
                        {
                            var messageForUser = Loc.GetString("interaction-target-hasclothing-message", ("target", Identity.Entity(targetEntity.Value, _entManager)));

                            if (_entManager.TryGetComponent<ActorComponent>(userEntity, out var actor))
                                _popupSystem.PopupEntity(messageForUser, userEntity, actor.PlayerSession, PopupType.Small);
                            return;
                        }
                    }
                }
            }

            bool hasStrapon = true;
            if (interactionPrototype.RequiresStrapon)
            {
                if (_entManager.TryGetComponent<InventoryComponent>(userEntity, out var inventory))
                {
                    if (!_inventorySystem.TryGetSlotEntity(userEntity, "belt", out var beltEntity, inventory) ||
                        !_entManager.TryGetComponent<StraponComponent>(beltEntity, out _))
                        hasStrapon = false;
                }
                else
                    hasStrapon = false;
            }

            if (!hasStrapon)
            {
                var message = Loc.GetString("interaction-missing-strapon-message");
                if (_entManager.TryGetComponent<ActorComponent>(userEntity, out var actor))
                    _popupSystem.PopupEntity(message, userEntity, actor.PlayerSession, PopupType.Small);

                return;
            }

            if (!HasRequiredHeldToy(userEntity, interactionPrototype))
                return;

            if (_entManager.TryGetComponent<CuffableComponent>(userEntity, out var cuffable))
            {
                if (!cuffable.CanStillInteract)
                {
                    var message = Loc.GetString("interaction-cuffed-message");
                    _popupSystem.PopupEntity(message, userEntity, userEntity, PopupType.Small);
                    return;
                }
            }

            if (interactionPrototype.DoAfterDelay > 0f)
            {
                TriggerDoAfter(userEntity, targetEntity ?? userEntity, interactionId, interactionPrototype.DoAfterDelay);
            }
            else
            {
                ExecuteInteraction(userEntity, targetEntity, interactionPrototype, useImportedPlaceholderPath, hideFromOthers, arousalHint);
            }
        }

        private void TriggerDoAfter(EntityUid user, EntityUid target, string interactionId, float delay)
        {
            // TODO Доделать делей
        }

        private void ExecuteInteraction(EntityUid user, EntityUid? target, InteractionPrototype interactionPrototype, bool prototype, bool hideFromOthers, int arousalHint)
        {
            int preferredIndex = GetRandomMessageIndex(interactionPrototype);

            if (target != null && interactionPrototype.TargetMessages.Count > 0)
            {
                if (preferredIndex < 0 || preferredIndex >= interactionPrototype.TargetMessages.Count)
                    preferredIndex = 0;

                string targetMessage;
                string otherMessage;
                if (prototype)
                {
                    targetMessage = ReplaceCustomPlaceholders(interactionPrototype.TargetMessages[preferredIndex], user, target.Value);
                    var otherTemplate = interactionPrototype.OtherMessages.Count > 0 ? interactionPrototype.OtherMessages[preferredIndex] : "";
                    otherMessage = ReplaceCustomPlaceholders(otherTemplate, user, target.Value);
                }
                else
                {
                    targetMessage = Loc.GetString(interactionPrototype.TargetMessages[preferredIndex], ("user", Identity.Entity(user, _entManager)));
                    otherMessage = Loc.GetString(interactionPrototype.OtherMessages.Count > 0 ? interactionPrototype.OtherMessages[preferredIndex] : "",
                        ("user", Identity.Entity(user, _entManager)), ("target", Identity.Entity(target.Value, _entManager)));
                }

                if (_entManager.TryGetComponent<ActorComponent>(target.Value, out var actor))
                    _popupSystem.PopupEntity(targetMessage, target.Value, actor.PlayerSession, PopupType.Small);

                if (!hideFromOthers)
                {
                    var filter = Filter.Local()
                        .AddAllPlayers()
                        .RemoveWhereAttachedEntity(uid => uid == user)
                        .RemoveWhereAttachedEntity(uid => uid == target.Value);

                    _popupSystem.PopupEntity(otherMessage, user, filter, false, PopupType.Small);
                }
            }

            if (interactionPrototype.UserMessages.Count > 0)
            {
                string emoteCommand;
                if (!prototype)
                {
                    if (preferredIndex < 0 || preferredIndex >= interactionPrototype.UserMessages.Count)
                        preferredIndex = 0;

                    emoteCommand = target == null
                        ? Loc.GetString(interactionPrototype.UserMessages[preferredIndex])
                        : Loc.GetString(interactionPrototype.UserMessages[preferredIndex], ("target", Identity.Entity(target.Value, _entManager)));
                }
                else
                {
                    emoteCommand = ReplaceCustomPlaceholders(interactionPrototype.UserMessages[0], user, target ?? user);
                }

                if (hideFromOthers && target != null)
                {
                    // Private emote: visible in chat only for user and target.
                    _chatSystem.SendPrivateEmotePair(user, target.Value, emoteCommand);
                }
                else
                {
                    if (_entManager.TryGetComponent<ActorComponent>(user, out var userActor))
                    {
                        var playerSession = userActor.PlayerSession;

                        _chatSystem.TrySendInGameICMessage(
                            source: user,
                            message: emoteCommand,
                            desiredType: InGameICChatType.Emote,
                            range: ChatTransmitRange.Normal,
                            hideLog: false,
                            player: playerSession
                        );
                    }
                }
            }

            var perceivedByOthers = interactionPrototype.SoundPerceivedByOthers && !hideFromOthers;
            PlayInteractionSound(interactionPrototype.InteractSound, user, target, perceivedByOthers);

            var arousal = interactionPrototype.EffectiveArousal;
            if (arousal <= 0 && arousalHint > 0)
                arousal = Math.Clamp(arousalHint, 0, 12);
            _arousal.RecordErpInteraction(user, target, interactionPrototype);
            _arousal.AddArousal(user, arousal);

            if (arousal > 0 && interactionPrototype.PartnerArousalMultiplier > 0f)
            {
                if (target != null)
                {
                    _arousal.AddPassivePartnerArousal(
                        target.Value,
                        arousal,
                        interactionPrototype.PartnerArousalMultiplier,
                        interactionPrototype.UseDelay);
                }
            }
        }

        private bool HasRequiredHeldToy(EntityUid user, InteractionPrototype interactionPrototype)
        {
            if (!interactionPrototype.RequiresVibrator && !interactionPrototype.RequiresDildo)
                return true;

            if (!_hands.TryGetActiveItem(user, out var held))
            {
                ShowToyMissingPopup(user, interactionPrototype);
                return false;
            }

            if (interactionPrototype.RequiresVibrator)
            {
                if (!_entManager.HasComponent<VibratorComponent>(held.Value))
                {
                    ShowToyMissingPopup(user, interactionPrototype);
                    return false;
                }

                if (_entManager.TryGetComponent<ItemToggleComponent>(held.Value, out var toggle) && !toggle.Activated)
                {
                    var message = Loc.GetString("interaction-vibrator-off");
                    if (_entManager.TryGetComponent<ActorComponent>(user, out var actor))
                        _popupSystem.PopupEntity(message, user, actor.PlayerSession, PopupType.Small);

                    return false;
                }
            }

            if (interactionPrototype.RequiresDildo)
            {
                if (!_entManager.TryGetComponent<SexToyComponent>(held.Value, out var sexToy) ||
                    !sexToy.Prototype.Any(proto => proto == "dildo"))
                {
                    ShowToyMissingPopup(user, interactionPrototype);
                    return false;
                }
            }

            return true;
        }

        private void ShowToyMissingPopup(EntityUid user, InteractionPrototype interactionPrototype)
        {
            var message = Loc.GetString(interactionPrototype.RequiresVibrator
                ? "interaction-missing-vibrator-message"
                : "interaction-missing-dildo-message");

            if (_entManager.TryGetComponent<ActorComponent>(user, out var actor))
                _popupSystem.PopupEntity(message, user, actor.PlayerSession, PopupType.Small);
        }

        private string ReplaceCustomPlaceholders(string template, EntityUid user, EntityUid target)
        {
            var userName = Name(Identity.Entity(user, _entManager));
            var targetName = Name(Identity.Entity(target, _entManager));

            return template
                .Replace("{ $user }", userName)
                .Replace("{ $target }", targetName)
                .Replace("$user", userName)
                .Replace("$target", targetName);
        }

        private int GetRandomMessageIndex(InteractionPrototype interactionPrototype)
        {
            var numberSuffixes = new List<int>();
            var numberPattern = new Regex(@"-(\d+)$");

            var allMessages = interactionPrototype.UserMessages
                .Concat(interactionPrototype.TargetMessages)
                .Concat(interactionPrototype.OtherMessages)
                .ToList();

            foreach (var message in allMessages)
            {
                var match = numberPattern.Match(message);
                if (match.Success)
                {
                    if (int.TryParse(match.Groups[1].Value, out var number))
                        numberSuffixes.Add(number);
                }
            }

            if (numberSuffixes.Count > 0)
            {
                var random = new Random();
                var randomIndex = random.Next(numberSuffixes.Min(), numberSuffixes.Max() + 1);
                return randomIndex - 1;
            }
            else
            {
                return allMessages.Count > 0 ? new Random().Next(allMessages.Count) : 0;
            }
        }

        private void PlayInteractionSound(SoundSpecifier? sound, EntityUid user, EntityUid? target, bool perceivedByOthers)
        {
            if (sound == null) return;

            if (perceivedByOthers)
            {
                _audio.PlayPvs(sound, target ?? user);
            }
            else
            {
                _audio.PlayEntity(sound, target == null ? Filter.Entities(user) : Filter.Entities(user, target.Value), target ?? user, false);
            }
        }

        private bool IsErpDenied(EntityUid uid)
        {
            return _entManager.TryGetComponent<DetailExaminableComponent>(uid, out var detail) &&
                   detail.ERPStatus == EnumERPStatus.NO;
        }

        private bool IsInteractionAnatomyAllowed(EntityUid user, EntityUid? target, InteractionPrototype prototype)
        {
            if (!TryComp<HumanoidAppearanceComponent>(user, out var userAppearance))
                return false;

            if (!IsAnatomyAllowed(user, userAppearance, prototype.AllowedGenders, prototype.Category))
                return false;

            if (target == null)
                return prototype.Solo;

            return TryComp<HumanoidAppearanceComponent>(target.Value, out var targetAppearance)
                && IsAnatomyAllowed(target.Value, targetAppearance, prototype.NearestAllowedGenders, prototype.Category);
        }

        private bool IsAnatomyAllowed(EntityUid uid, HumanoidAppearanceComponent appearance, List<string>? genders, string category)
        {
            if (genders?.Contains("all") == true)
                return true;
            if (!TryComp<AdultAnatomyComponent>(uid, out var anatomy))
                return genders?.Contains(appearance.Sex.ToString()) == true;
            if (genders?.Contains("Male") == true && anatomy.HasPenis)
                return true;
            if (genders?.Contains("Female") == true)
                return category.Equals("chest", StringComparison.OrdinalIgnoreCase)
                    ? anatomy.HasBreasts
                    : anatomy.HasVagina;
            return false;
        }
    }
}

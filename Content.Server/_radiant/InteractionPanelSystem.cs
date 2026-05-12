using System.Linq;
using System.Text.RegularExpressions;
using Content.Shared.Chat.Prototypes;
using Content.Shared.Cuffs.Components;
using Content.Shared.Ghost;
using Content.Shared.Humanoid;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Components;
using Content.Shared.Physics;
using Content.Shared.Popups;
using Content.Shared.ERP.Components;
using Content.Server.Chat.Systems;
using Content.Server._radiant.Arousal;
using Content.Server.Popups;
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
            if (target == null) return;

            interactionId = interactionId.Trim();

            var userEntity = _entManager.GetEntity(user);
            if (HasComp<GhostComponent>(userEntity) && !HasComp<HumanoidAppearanceComponent>(userEntity))
                return;

            if (_entManager.TryGetComponent<MobThresholdsComponent>(userEntity, out var userThresholds) &&
                userThresholds.CurrentThresholdState != MobState.Alive &&
                userThresholds.CurrentThresholdState != MobState.Invalid)
                return;

            var targetEntity = _entManager.GetEntity(target.Value);
            if (_entManager.TryGetComponent<MobThresholdsComponent>(targetEntity, out var targetThresholds) &&
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

            if (_entManager.TryGetComponent<TransformComponent>(userEntity, out var userTransform) &&
                _entManager.TryGetComponent<TransformComponent>(targetEntity, out var targetTransform))
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

            if (_lastInteractionTimes.TryGetValue(target.Value, out var lastInteractionTime))
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

            _lastInteractionTimes[target.Value] = DateTime.UtcNow;
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

                if (TryComp<InventoryComponent>(targetEntity, out var targetInventory))
                {
                    var requiredSlots = interactionPrototype.RequiredClothingSlots ?? Enumerable.Empty<string>();
                    var oneRequiredSlots = interactionPrototype.OneRequiredClothingSlots ?? Enumerable.Empty<string>();

                    var allSlots = requiredSlots.Concat(oneRequiredSlots);

                    foreach (var slot in allSlots)
                    {
                        if (_inventorySystem.TryGetSlotEntity(targetEntity, slot, out _, targetInventory))
                        {
                            var targetEntityValue = _entManager.GetEntity(target.Value);
                            var messageForUser = Loc.GetString("interaction-target-hasclothing-message", ("target", Identity.Entity(targetEntityValue, _entManager)));

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
                TriggerDoAfter(userEntity, targetEntity, interactionId, interactionPrototype.DoAfterDelay);
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

        private void ExecuteInteraction(EntityUid user, EntityUid target, InteractionPrototype interactionPrototype, bool prototype, bool hideFromOthers, int arousalHint)
        {
            int preferredIndex = GetRandomMessageIndex(interactionPrototype);

            if (interactionPrototype.TargetMessages.Count > 0)
            {
                if (preferredIndex < 0 || preferredIndex >= interactionPrototype.TargetMessages.Count)
                    preferredIndex = 0;

                string targetMessage;
                string otherMessage;
                if (prototype)
                {
                    targetMessage = ReplaceCustomPlaceholders(interactionPrototype.TargetMessages[preferredIndex], user, target);
                    var otherTemplate = interactionPrototype.OtherMessages.Count > 0 ? interactionPrototype.OtherMessages[preferredIndex] : "";
                    otherMessage = ReplaceCustomPlaceholders(otherTemplate, user, target);
                }
                else
                {
                    targetMessage = Loc.GetString(interactionPrototype.TargetMessages[preferredIndex], ("user", Identity.Entity(user, _entManager)));
                    otherMessage = Loc.GetString(interactionPrototype.OtherMessages.Count > 0 ? interactionPrototype.OtherMessages[preferredIndex] : "",
                        ("user", Identity.Entity(user, _entManager)), ("target", Identity.Entity(target, _entManager)));
                }

                if (_entManager.TryGetComponent<ActorComponent>(target, out var actor))
                    _popupSystem.PopupEntity(targetMessage, target, actor.PlayerSession, PopupType.Small);

                if (!hideFromOthers)
                {
                    var filter = Filter.Local()
                        .AddAllPlayers()
                        .RemoveWhereAttachedEntity(uid => uid == user)
                        .RemoveWhereAttachedEntity(uid => uid == target);

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

                    emoteCommand = Loc.GetString(interactionPrototype.UserMessages[preferredIndex], ("target", Identity.Entity(target, _entManager)));
                }
                else
                {
                    emoteCommand = ReplaceCustomPlaceholders(interactionPrototype.UserMessages[0], user, target);
                }

                if (hideFromOthers)
                {
                    // Private emote: visible in chat only for user and target.
                    _chatSystem.SendPrivateEmotePair(user, target, emoteCommand);
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
            _arousal.AddArousal(user, arousal);

            if (arousal > 0 && interactionPrototype.PartnerArousalMultiplier > 0f)
            {
                _arousal.AddPassivePartnerArousal(
                    target,
                    arousal,
                    interactionPrototype.PartnerArousalMultiplier,
                    interactionPrototype.UseDelay);
            }
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

        private void PlayInteractionSound(SoundSpecifier? sound, EntityUid user, EntityUid target, bool perceivedByOthers)
        {
            if (sound == null) return;

            if (perceivedByOthers)
            {
                _audio.PlayPvs(sound, target);
            }
            else
            {
                _audio.PlayEntity(sound, Filter.Entities(user, target), target, false);
            }
        }
    }
}
using Content.Shared.DoAfter;
using Content.Server.Chat.Systems;
using Content.Server._radiant.Arousal;
using Content.Server.Speech.Components;
using Content.Server.Speech.EntitySystems;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Events;
using Content.Shared.ERP.Components;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Verbs;
using Content.Shared.Popups;
using Content.Shared.Speech;
using Content.Shared.Toggleable;
using Content.Server.Popups;
using Robust.Shared.GameStates;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Player;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Server.Vibrator.System
{
    public class VibratorUsageSystem : EntitySystem
    {
        [Dependency] private readonly IEntityManager _entManager = default!;
        [Dependency] private readonly PopupSystem _popupSystem = default!;
        [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
        [Dependency] private readonly InventorySystem _inventorySystem = default!;
        [Dependency] private readonly SharedDeviceLinkSystem _deviceLink = default!;
        [Dependency] private readonly ItemToggleSystem _itemToggle = default!;
        [Dependency] private readonly StutteringSystem _stuttering = default!;
        [Dependency] private readonly ChatSystem _chat = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
        [Dependency] private readonly ArousalSystem _arousal = default!;
        [Dependency] private readonly IGameTiming _timing = default!;

        private readonly StutteringAccentComponent _plugStutter = new();

        public override void Initialize()
        {
            base.Initialize();
            SubscribeLocalEvent<VibratorComponent, AfterInteractEvent>(OnInteract);
            SubscribeLocalEvent<VibratorComponent, VibratorDoAfterEvent>(OnDoAfter);
            SubscribeLocalEvent<VibratorComponent, ComponentInit>(OnComponentInit);
            SubscribeLocalEvent<VibratorComponent, SignalReceivedEvent>(OnSignalReceived);
            SubscribeLocalEvent<VibratorComponent, ItemToggledEvent>(OnToggled);
            SubscribeLocalEvent<VibratorComponent, GetVerbsEvent<AlternativeVerb>>(OnGetModeVerbs);
            SubscribeLocalEvent<VibratorComponent, MapInitEvent>(OnMapInit);
            SubscribeLocalEvent<AccentGetEvent>(OnAccentGet);
        }

        private void OnMapInit(Entity<VibratorComponent> entity, ref MapInitEvent args)
        {
            UpdateVisuals(entity);
        }

        private void OnToggled(Entity<VibratorComponent> entity, ref ItemToggledEvent args)
        {
            UpdateVisuals(entity, args.Activated);
            entity.Comp.NextPassiveArousal = args.Activated
                ? _timing.CurTime + GetArousalInterval(entity.Comp)
                : TimeSpan.Zero;
            entity.Comp.NextPassiveMoan = args.Activated
                ? _timing.CurTime + GetMoanInterval(entity.Comp)
                : TimeSpan.Zero;
        }

        public override void Update(float frameTime)
        {
            base.Update(frameTime);

            var query = EntityQueryEnumerator<VibratorComponent, ItemToggleComponent, TransformComponent>();
            while (query.MoveNext(out var uid, out var vibrator, out var toggle, out var xform))
            {
                if (!toggle.Activated || vibrator.Mode is VibratorMode.Off or VibratorMode.Low)
                {
                    vibrator.NextPassiveArousal = TimeSpan.Zero;
                    vibrator.NextPassiveMoan = TimeSpan.Zero;
                    continue;
                }

                var wearer = xform.ParentUid;
                if (!wearer.Valid ||
                    !_inventorySystem.TryGetSlotEntity(wearer, "plug", out var plug) ||
                    plug != uid)
                {
                    vibrator.NextPassiveArousal = TimeSpan.Zero;
                    vibrator.NextPassiveMoan = TimeSpan.Zero;
                    continue;
                }

                if (vibrator.NextPassiveArousal == TimeSpan.Zero)
                {
                    vibrator.NextPassiveArousal = _timing.CurTime + GetArousalInterval(vibrator);
                    continue;
                }

                if (_timing.CurTime >= vibrator.NextPassiveArousal)
                {
                    _arousal.AddArousal(wearer, vibrator.PassiveArousalAmount);
                    vibrator.NextPassiveArousal = _timing.CurTime + GetArousalInterval(vibrator);
                }

                if (vibrator.NextPassiveMoan == TimeSpan.Zero)
                {
                    vibrator.NextPassiveMoan = _timing.CurTime + GetMoanInterval(vibrator);
                    continue;
                }

                if (_timing.CurTime < vibrator.NextPassiveMoan)
                    continue;

                _chat.TryEmoteWithChat(wearer, "Ston");
                vibrator.NextPassiveMoan = _timing.CurTime + GetMoanInterval(vibrator);
            }
        }

        private static TimeSpan GetMoanInterval(VibratorComponent component)
        {
            return component.Mode == VibratorMode.Hard
                ? component.HardMoanInterval
                : component.MediumMoanInterval;
        }

        private static TimeSpan GetArousalInterval(VibratorComponent component)
        {
            return component.Mode == VibratorMode.Hard
                ? component.HardArousalInterval
                : component.MediumArousalInterval;
        }

        private void OnGetModeVerbs(Entity<VibratorComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
        {
            if (!args.CanAccess || !args.CanInteract)
                return;

            AddModeVerb(entity, args.User, VibratorMode.Low, "vibrator-mode-low", ref args);
            AddModeVerb(entity, args.User, VibratorMode.Medium, "vibrator-mode-medium", ref args);
            AddModeVerb(entity, args.User, VibratorMode.Hard, "vibrator-mode-hard", ref args);

            if (entity.Comp.Mode == VibratorMode.Hard)
                return;

            var user = args.User;
            var muted = entity.Comp.Muted;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString(muted ? "vibrator-sound-enable" : "vibrator-sound-disable"),
                Act = () => SetMuted(entity, user, !muted),
            });
        }

        private void SetMuted(Entity<VibratorComponent> entity, EntityUid user, bool muted)
        {
            if (muted && entity.Comp.Mode == VibratorMode.Hard)
                return;

            if (entity.Comp.Muted == muted)
                return;

            var wasActive = TryComp<ItemToggleComponent>(entity.Owner, out var toggle) && toggle.Activated;
            if (wasActive)
                _itemToggle.TryDeactivate(entity.Owner, user, predicted: false, showPopup: false);

            entity.Comp.Muted = muted;
            Dirty(entity);

            var sound = EnsureComp<ItemToggleActiveSoundComponent>(entity.Owner);
            sound.ActiveSound = muted ? null : entity.Comp.ActiveSound;
            Dirty(entity.Owner, sound);

            if (wasActive)
                _itemToggle.TryActivate(entity.Owner, user, predicted: false, showPopup: false);

            _popupSystem.PopupEntity(
                Loc.GetString(muted ? "vibrator-sound-muted" : "vibrator-sound-enabled"),
                entity.Owner,
                user,
                PopupType.Small);
        }

        private void AddModeVerb(
            Entity<VibratorComponent> entity,
            EntityUid user,
            VibratorMode mode,
            LocId text,
            ref GetVerbsEvent<AlternativeVerb> args)
        {
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString(text),
                Disabled = entity.Comp.Mode == mode,
                Act = () => SetMode(entity, user, mode),
            });
        }

        private void SetMode(Entity<VibratorComponent> entity, EntityUid user, VibratorMode mode)
        {
            if (mode == VibratorMode.Hard && entity.Comp.Muted)
                SetMuted(entity, user, false);

            entity.Comp.Mode = mode;
            entity.Comp.NextPassiveArousal = mode is VibratorMode.Medium or VibratorMode.Hard
                ? _timing.CurTime + GetArousalInterval(entity.Comp)
                : TimeSpan.Zero;
            entity.Comp.NextPassiveMoan = mode is VibratorMode.Medium or VibratorMode.Hard
                ? _timing.CurTime + GetMoanInterval(entity.Comp)
                : TimeSpan.Zero;
            Dirty(entity);
            UpdateVisuals(entity);
            _popupSystem.PopupEntity(
                Loc.GetString("vibrator-mode-set", ("mode", Loc.GetString($"vibrator-mode-{mode.ToString().ToLowerInvariant()}"))),
                entity.Owner,
                user,
                PopupType.Small);
        }

        private void UpdateVisuals(Entity<VibratorComponent> entity, bool? activated = null)
        {
            var isActive = activated ?? TryComp<ItemToggleComponent>(entity.Owner, out var toggle) && toggle.Activated;
            _appearance.SetData(entity.Owner,
                ToggleableVisuals.Color,
                (isActive ? entity.Comp.Mode : VibratorMode.Off).ToString());
        }

        private void OnAccentGet(AccentGetEvent args)
        {
            if (!_inventorySystem.TryGetSlotEntity(args.Entity, "plug", out var plug) ||
                !TryComp<VibratorComponent>(plug, out var vibrator) ||
                !TryComp<ItemToggleComponent>(plug, out var toggle) ||
                !toggle.Activated ||
                vibrator.Mode != VibratorMode.Hard)
            {
                return;
            }

            args.Message = _stuttering.Accentuate(args.Message, _plugStutter);

            if (_random.Prob(vibrator.MoanChance))
                _chat.TryEmoteWithChat(args.Entity, "Ston");
        }

        private void OnComponentInit(Entity<VibratorComponent> entity, ref ComponentInit args)
        {
            _deviceLink.EnsureSinkPorts(entity.Owner,
                entity.Comp.TogglePort,
                entity.Comp.OnPort,
                entity.Comp.OffPort);

            var sound = EnsureComp<ItemToggleActiveSoundComponent>(entity.Owner);
            sound.ActiveSound = entity.Comp.Muted ? null : entity.Comp.ActiveSound;
            Dirty(entity.Owner, sound);
        }

        private void OnSignalReceived(Entity<VibratorComponent> entity, ref SignalReceivedEvent args)
        {
            if (args.Port == entity.Comp.OnPort)
                _itemToggle.TryActivate(entity.Owner, predicted: false, showPopup: false);
            else if (args.Port == entity.Comp.OffPort)
                _itemToggle.TryDeactivate(entity.Owner, predicted: false, showPopup: false);
            else if (args.Port == entity.Comp.TogglePort)
                _itemToggle.Toggle(entity.Owner, predicted: false, showPopup: false);
        }

        private void OnInteract(Entity<VibratorComponent> entity, ref AfterInteractEvent args)
        {
            if (args.Handled)
                return;

            var user = args.User;

            if (!args.CanReach || args.Target is not { Valid: true } target)
                return;

            if (_entManager.TryGetComponent<ItemToggleComponent>(entity.Owner, out var toggle))
            {
                if (toggle.Activated)
                {
                    StartDoAfter(entity.Owner, user, target);
                }
                else
                {
                    var noToggleMessage = Loc.GetString("interaction-vibrator-off");
                    if (_entManager.TryGetComponent<ActorComponent>(user, out var actor))
                        _popupSystem.PopupEntity(noToggleMessage, user, actor.PlayerSession, PopupType.Small);
                }
            }

            args.Handled = true;
        }

        private void StartDoAfter(EntityUid vibratorEntity, EntityUid user, EntityUid target)
        {
            var requiredClothingSlots = new[] { "jumpsuit", "outerClothing", "underwearb" };

            if (TryComp<InventoryComponent>(target, out var inventory))
            {
                foreach (var slot in requiredClothingSlots)
                {
                    if (_inventorySystem.TryGetSlotEntity(target, slot, out var slotEntity, inventory))
                    {
                        var message = Loc.GetString("interaction-slot-occupied-message");
                        if (_entManager.TryGetComponent<ActorComponent>(user, out var actor))
                            _popupSystem.PopupEntity(message, user, actor.PlayerSession, PopupType.Small);
                        return;
                    }
                }
            }

            var doAfterEventArgs = new DoAfterArgs(EntityManager, user, 3f, new VibratorDoAfterEvent(), vibratorEntity, target: target)
            {
                BreakOnMove = true,
                BreakOnDamage = true,
                NeedHand = true,
            };

            _doAfter.TryStartDoAfter(doAfterEventArgs);
        }

        private void OnDoAfter(Entity<VibratorComponent> entity, ref VibratorDoAfterEvent args)
        {
            if (args.Handled || args.Cancelled)
                return;

            args.Handled = true;

            if (args.Args.Target is { Valid: true } target)
            {
                UseVibrator(entity.Owner, args.Args.User, target);
            }
        }

        private void UseVibrator(EntityUid vibratorEntity, EntityUid user, EntityUid target)
        {
            if (!_entManager.TryGetComponent<VibratorComponent>(vibratorEntity, out var vibratorComponent))
                return;

            string userName = "";
            if (_entManager.TryGetComponent<MetaDataComponent>(user, out var metaDataComponent))
                userName = metaDataComponent.EntityName;

            var noHumanoidMessage = Loc.GetString("interaction-impossible");
            var invalidGenderMessage = Loc.GetString("interaction-cant-do-this");
            var invalidSpeciesMessage = Loc.GetString("interaction-no-race");

            if (!_entManager.TryGetComponent<HumanoidAppearanceComponent>(target, out var targetAppearance))
            {
                if (_entManager.TryGetComponent<ActorComponent>(user, out var userActor))
                    _popupSystem.PopupEntity(noHumanoidMessage, user, userActor.PlayerSession, PopupType.Small);
                return;
            }

            string messageUser = "";
            string messageTarget = "";

            var random = new Random();

            var vibratorUserMessagesVox = new[]
            {
                Loc.GetString("interaction-vibrator-user-vox-1"),
                Loc.GetString("interaction-vibrator-user-vox-2"),
            };

            var vibratorTargetMessagesVox = new[]
            {
                Loc.GetString("interaction-vibrator-target-vox-1", ("user", userName)),
                Loc.GetString("interaction-vibrator-target-vox-2", ("user", userName)),
            };

            var vibratorUserMessagesMale = new[]
            {
                Loc.GetString("interaction-vibrator-user-anal-1"),
                Loc.GetString("interaction-vibrator-user-anal-2"),
                Loc.GetString("interaction-vibrator-user-dick-1"),
            };

            var vibratorTargetMessagesMale = new[]
            {
                Loc.GetString("interaction-vibrator-target-anal-1", ("user", userName)),
                Loc.GetString("interaction-vibrator-target-anal-2", ("user", userName)),
                Loc.GetString("interaction-vibrator-target-dick-1", ("user", userName)),
            };

            var vibratorUserMessagesFemale = new[]
            {
                Loc.GetString("interaction-vibrator-user-anal-1"),
                Loc.GetString("interaction-vibrator-user-anal-2"),
                Loc.GetString("interaction-vibrator-user-vagina-1"),
            };

            var vibratorTargetMessagesFemale = new[]
            {
                Loc.GetString("interaction-vibrator-target-anal-1", ("user", userName)),
                Loc.GetString("interaction-vibrator-target-anal-2", ("user", userName)),
                Loc.GetString("interaction-vibrator-target-vagina-1", ("user", userName)),
            };

            switch (targetAppearance.Species)
            {
                case "Vox":
                    messageUser = vibratorUserMessagesVox[random.Next(vibratorUserMessagesVox.Length)];
                    messageTarget = vibratorTargetMessagesVox[random.Next(vibratorTargetMessagesVox.Length)];
                    break;

                case "Diona":
                case "Arachnid":
                    if (_entManager.TryGetComponent<ActorComponent>(user, out var userActorSpecies))
                        _popupSystem.PopupEntity(invalidSpeciesMessage, user, userActorSpecies.PlayerSession, PopupType.Small);
                    return;

                default:
                    if (targetAppearance.Sex == Sex.Male)
                    {
                        messageUser = vibratorUserMessagesMale[random.Next(vibratorUserMessagesMale.Length)];
                        messageTarget = vibratorTargetMessagesMale[random.Next(vibratorTargetMessagesMale.Length)];
                    }
                    else if (targetAppearance.Sex == Sex.Female)
                    {
                        messageUser = vibratorUserMessagesFemale[random.Next(vibratorUserMessagesFemale.Length)];
                        messageTarget = vibratorTargetMessagesFemale[random.Next(vibratorTargetMessagesFemale.Length)];
                    }
                    else
                    {
                        if (_entManager.TryGetComponent<ActorComponent>(user, out var userActorGender))
                            _popupSystem.PopupEntity(invalidGenderMessage, user, userActorGender.PlayerSession, PopupType.Small);
                        return;
                    }
                    break;
            }

            if (_entManager.TryGetComponent<ActorComponent>(user, out var finalUserActor))
                _popupSystem.PopupEntity(messageUser, user, finalUserActor.PlayerSession, PopupType.Small);

            if (target != user && _entManager.TryGetComponent<ActorComponent>(target, out var finalTargetActor))
                _popupSystem.PopupEntity(messageTarget, target, finalTargetActor.PlayerSession, PopupType.Small);
        }
    }
}

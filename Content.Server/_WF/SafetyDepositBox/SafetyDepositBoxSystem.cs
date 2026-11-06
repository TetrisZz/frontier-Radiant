using System.IO;
using System.Linq;
using Content.Server._radiant.Storage;
using Content.Server._HL.Shipyard;
using System.Threading.Tasks;
using Content.Server.Administration.Logs;
using Content.Server.Database;
using Content.Server.GameTicking;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Server.Preferences.Managers;
using Content.Server._NF.Bank;
using Content.Shared._NF.Bank.Components;
using Content.Shared._WF.SafetyDepositBox.BUI;
using Content.Shared._WF.SafetyDepositBox.Components;
using Content.Shared._WF.SafetyDepositBox.Events;
using Content.Shared.Database;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Labels.Components;
using Content.Shared.Labels.EntitySystems;
using Robust.Server.GameObjects;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown.Mapping;

namespace Content.Server._WF.SafetyDepositBox;

public sealed partial class SafetyDepositBoxSystem : EntitySystem
{
    private readonly HashSet<Guid> _boxOperations = new();
    private readonly HashSet<EntityUid> _depositingBoxes = new();
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedAudioSystem _audio = default!;
    [Dependency] private PopupSystem _popup = default!;
    [Dependency] private BankSystem _bankSystem = default!;
    [Dependency] private UserInterfaceSystem _uiSystem = default!;
    [Dependency] private IAdminLogManager _adminLogger = default!;
    [Dependency] private HandsSystem _hands = default!;
    [Dependency] private TransformSystem _transform = default!;
    [Dependency] private IServerDbManager _dbManager = default!;
    [Dependency] private SharedStorageSystem _storage = default!;
    [Dependency] private ItemSlotsSystem _itemSlots = default!;
    [Dependency] private LabelSystem _label = default!;
    [Dependency] private IServerPreferencesManager _prefsManager = default!;
    [Dependency] private GameTicker _gameTicker = default!;
    [Dependency] private IComponentFactory _componentFactory = default!;
    [Dependency] private MapLoaderSystem _loader = default!;
    [Dependency] private ISerializationManager _serialization = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<SafetyDepositConsoleComponent, ComponentInit>(OnConsoleInit);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, BoundUIOpenedEvent>(OnUIOpen);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, SafetyDepositPurchaseMessage>(OnPurchase);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, SafetyDepositDepositMessage>(OnDeposit);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, SafetyDepositWithdrawMessage>(OnWithdraw);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, SafetyDepositReclaimMessage>(OnReclaim);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, EntInsertedIntoContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<SafetyDepositConsoleComponent, EntRemovedFromContainerMessage>(OnSlotChanged);
        SubscribeLocalEvent<SafetyDepositBoxComponent, ContainerIsInsertingAttemptEvent>(OnInsertDuringDeposit);
        SubscribeLocalEvent<SafetyDepositBoxComponent, ContainerIsRemovingAttemptEvent>(OnRemoveDuringDeposit);
    }

    private void OnConsoleInit(EntityUid uid, SafetyDepositConsoleComponent component, ComponentInit args)
    {
        _itemSlots.AddItemSlot(uid, SafetyDepositConsoleComponent.BoxSlotId, component.BoxSlot);
    }

    private void OnUIOpen(EntityUid uid, SafetyDepositConsoleComponent component, BoundUIOpenedEvent args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        UpdateUI(uid, component, player);
    }

    public void RefreshCareerUi(EntityUid console, EntityUid player)
    {
        if (TryComp<SafetyDepositConsoleComponent>(console, out var terminal) && Exists(player))
            UpdateUI(console, terminal, player);
    }

    private async void UpdateUI(EntityUid consoleUid, SafetyDepositConsoleComponent component, EntityUid player)
    {
        if (!TryComp<ActorComponent>(player, out var actor))
            return;

        var userId = actor.PlayerSession.UserId;
        if (!_prefsManager.TryGetCachedPreferences(userId, out var prefs))
            return;

        var characterIndex = prefs.SelectedCharacterIndex;

        // Get all boxes owned by this character from database
        var ownedBoxes = await _dbManager.GetPlayerSafetyDepositBoxes(userId.UserId, characterIndex);

        var boxInfoList = new List<SafetyDepositBoxInfo>();
        foreach (var box in ownedBoxes)
        {
            // A box is considered deposited if:
            // - It has never been withdrawn (!LastWithdrawn.HasValue), OR
            // - It was withdrawn in the current round and still has items
            // A box is considered lost if it was withdrawn in a previous round and has no items
            bool isDeposited;
            if (!box.LastWithdrawn.HasValue)
                isDeposited = true;
            else if (box.LastWithdrawnRoundId.HasValue && box.LastWithdrawnRoundId.Value != _gameTicker.RoundId)
            {
                // Withdrawn in a previous round - lost regardless of items
                isDeposited = false;
            }
            else
            {
                // Withdrawn in current round - deposited only if it has items
                isDeposited = box.Items.Count > 0;
            }

            boxInfoList.Add(new (
                box.BoxId,
                box.OwnerName,
                isDeposited,
                box.Nickname,
                box.ProtoId,
                box.LastWithdrawn,
                box.LastWithdrawnRoundId,
                _boxOperations.Contains(box.BoxId)
            ));
        }

        var boxInSlot = component.BoxSlot.Item;
        SafetyDepositBoxInfo? boxInSlotInfo = null;

        if (boxInSlot != null && TryComp<SafetyDepositBoxComponent>(boxInSlot, out var boxComp) && boxComp.BoxId.HasValue)
        {
            // Get label if it exists
            string? nickname = null;
            if (TryComp<LabelComponent>(boxInSlot.Value, out var labelComp))
            {
                nickname = labelComp.CurrentLabel;
            }

            if (!TryPrototype(boxInSlot.Value, out var boxProto))
                return;

            boxInSlotInfo = new SafetyDepositBoxInfo(
                boxComp.BoxId.Value,
                boxComp.OwnerName ?? "Unknown",
                false,
                nickname,
                boxProto.ToString(),
                null,
                null
            );
        }

        var state = new SafetyDepositConsoleState(
            boxInfoList,
            0, // No cash display needed anymore
            boxInSlot != null,
            boxInSlotInfo,
            GetBoxCost(component.SmallBoxProto),
            GetBoxCost(component.MediumBoxProto),
            GetBoxCost(component.LargeBoxProto),
            _gameTicker.RoundId
        );

        if (!Exists(consoleUid) || TerminatingOrDeleted(consoleUid) || !Exists(player))
            return;
        state.Career = EntityManager.System<Content.Server._radiant.Careers.CareerRewardSystem>()
            .GetSafeState(consoleUid, player);
        _uiSystem.SetUiState(consoleUid, SafetyDepositConsoleUiKey.Key, state);
    }

    private void OnPurchase(EntityUid uid, SafetyDepositConsoleComponent component, SafetyDepositPurchaseMessage args)
    {
        int cost;
        EntityPrototype prototypeId;
        if (args.Actor is not { Valid: true } player)
            return;

        if (!TryComp<ActorComponent>(player, out var actor))
            return;

        if (_prototypeManager.TryIndex(args.BoxProto, out var proto) && proto.TryGetComponent<SafetyDepositBoxComponent>(out var boxComponent, _componentFactory))
        {
            cost = boxComponent.Cost;
            prototypeId = proto;
        }
        else
        {
            ConsolePopup(player, "Error: Invalid box size.");
            PlayDenySound(uid, component);
            return;
        }

        // Check bank account
        if (!TryComp<BankAccountComponent>(player, out var bank))
        {
            ConsolePopup(player, "Error: No bank account found.");
            PlayDenySound(uid, component);
            return;
        }

        if (bank.Balance < cost)
        {
            ConsolePopup(player, $"Insufficient funds. You need ${cost:N0}, but only have ${bank.Balance:N0}.");
            PlayDenySound(uid, component);
            return;
        }

        // Withdraw from bank
        if (!_bankSystem.TryBankWithdraw(player, cost))
        {
            ConsolePopup(player, "Transaction failed.");
            PlayDenySound(uid, component);
            return;
        }

        // Create the box in the database
        var userId = actor.PlayerSession.UserId;
        if (!_prefsManager.TryGetCachedPreferences(userId, out var prefs))
        {
            ConsolePopup(player, "Error: Could not load character data.");
            PlayDenySound(uid, component);
            return;
        }

        var characterIndex = prefs.SelectedCharacterIndex;
        var characterName = MetaData(player).EntityName;

        PurchaseBoxAsync(uid, component, player, userId.UserId, characterIndex, characterName, prototypeId, cost);
    }

    // got tired of doing this
    public int GetBoxCost(EntProtoId boxProto)
    {
        if (_prototypeManager.TryIndex(boxProto, out var proto) &&
            proto.TryGetComponent<SafetyDepositBoxComponent>(out var boxComponent, _componentFactory))
            return boxComponent.Cost;
        else
            return 0;
    }

    private async void PurchaseBoxAsync(
        EntityUid consoleUid,
        SafetyDepositConsoleComponent component,
        EntityUid player,
        Guid userId,
        int characterIndex,
        string characterName,
        EntityPrototype prototypeId,
        int cost)
    {
        // Create box in database
        var box = await _dbManager.PurchaseSafetyDepositBox(userId, characterIndex, characterName, prototypeId.ID);

        // Spawn the physical box
        var boxEntity = Spawn(prototypeId.ID, Transform(player).Coordinates);
        var boxComp = EnsureComp<SafetyDepositBoxComponent>(boxEntity);

        boxComp.BoxId = box.BoxId;
        boxComp.OwnerId = userId;
        boxComp.CharacterIndex = characterIndex;
        boxComp.OwnerName = characterName;
        Dirty(boxEntity, boxComp);

        // Try to put it in player's hands
        if (!_hands.TryPickupAnyHand(player, boxEntity))
        {
            _transform.SetLocalRotation(boxEntity, Angle.Zero);
        }

        // Mark the box as withdrawn so it shows "In World" in the UI
        await _dbManager.ClearSafetyDepositBoxItems(box.BoxId, _gameTicker.RoundId);

        ConsolePopup(player, $"Safety deposit box purchased! Box ID: {box.BoxId.ToString()[..8]}...");
        PlayConfirmSound(consoleUid, component);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):actor} purchased safety deposit box {box.BoxId} for {cost} credits");

        UpdateUI(consoleUid, component, player);
    }

    private async void OnDeposit(EntityUid uid, SafetyDepositConsoleComponent component, SafetyDepositDepositMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!TryComp<ActorComponent>(player, out var actor))
            return;

        // Check if there's a box in the slot
        var boxEntity = component.BoxSlot.Item;
        if (boxEntity == null)
        {
            ConsolePopup(player, "Please insert a safety deposit box.");
            PlayDenySound(uid, component);
            return;
        }

        if (!TryComp<SafetyDepositBoxComponent>(boxEntity.Value, out var boxComp) || !boxComp.BoxId.HasValue)
        {
            ConsolePopup(player, "Invalid safety deposit box.");
            PlayDenySound(uid, component);
            return;
        }

        // Verify ownership
        var userId = actor.PlayerSession.UserId;
        if (!_prefsManager.TryGetCachedPreferences(userId, out var prefs))
        {
            ConsolePopup(player, "Error: Could not load character data.");
            PlayDenySound(uid, component);
            return;
        }

        var characterIndex = prefs.SelectedCharacterIndex;
        if (boxComp.OwnerId != userId.UserId || boxComp.CharacterIndex != characterIndex)
        {
            ConsolePopup(player, "This box does not belong to you.");
            PlayDenySound(uid, component);
            return;
        }

        // Serialize the contents
        if (!TryComp<StorageComponent>(boxEntity.Value, out var storageComp))
        {
            ConsolePopup(player, "Error: Box has no storage.");
            PlayDenySound(uid, component);
            return;
        }

        if (IdentityDocumentPersistence.FindDocuments(EntityManager, boxEntity.Value).Any())
        {
            ConsolePopup(player, Loc.GetString("safety-deposit-remove-identity-documents"));
            return;
        }
        var boxId = boxComp.BoxId.Value;
        if (!_boxOperations.Add(boxId))
            return;

        _depositingBoxes.Add(boxEntity.Value);
        try
        {
            await DepositBoxAsync(uid, component, player, boxEntity.Value, boxComp, storageComp);
        }
        catch (Exception exception)
        {
            Log.Error($"Failed depositing safety deposit box {boxId}: {exception}");
            if (Exists(player))
                ConsolePopup(player, Loc.GetString("safety-deposit-save-failed"));
        }
        finally
        {
            _depositingBoxes.Remove(boxEntity.Value);
            _boxOperations.Remove(boxId);
            if (Exists(uid) && Exists(player))
                UpdateUI(uid, component, player);
        }
    }

    private void OnInsertDuringDeposit(EntityUid uid, SafetyDepositBoxComponent component, ContainerIsInsertingAttemptEvent args)
    {
        if (_depositingBoxes.Contains(uid))
            args.Cancel();
    }

    private void OnRemoveDuringDeposit(EntityUid uid, SafetyDepositBoxComponent component, ContainerIsRemovingAttemptEvent args)
    {
        if (_depositingBoxes.Contains(uid))
            args.Cancel();
    }

    private async Task DepositBoxAsync(
        EntityUid consoleUid,
        SafetyDepositConsoleComponent component,
        EntityUid player,
        EntityUid boxEntity,
        SafetyDepositBoxComponent boxComp,
        StorageComponent storageComp)
    {
        var storedBox = await _dbManager.GetSafetyDepositBox(boxComp.BoxId!.Value);
        if (storedBox == null || storedBox.OwnerUserId != boxComp.OwnerId || storedBox.CharacterIndex != boxComp.CharacterIndex)
            throw new InvalidOperationException($"Invalid database slot for safety deposit box {boxComp.BoxId}.");
        if (!storedBox.LastWithdrawn.HasValue)
            throw new InvalidOperationException($"Safety deposit box {boxComp.BoxId} is already deposited; refusing to overwrite it with a stale physical container.");

        var entityDataList = new List<string>();

        Log.Info($"DepositBoxAsync: Box has {storageComp.Container.ContainedEntities.Count} items");

        // Serialize each item in the box - store prototype + component data
        foreach (var item in storageComp.Container.ContainedEntities)
        {
            try
            {
                Log.Info($"Serializing item: {ToPrettyString(item)}");
                using var writer = new StringWriter();
                if (!_loader.TrySaveEntity(item, writer) || string.IsNullOrWhiteSpace(writer.ToString()))
                    throw new InvalidDataException("Entity serialization did not produce a complete snapshot.");
                if (!storageComp.StoredItems.TryGetValue(item, out var location))
                    throw new InvalidDataException("Item has no position in the deposit box.");
                var snapshot = ShipSaveYamlSanitizer.ScrubStoredItemYaml(writer.ToString(), out var changes, root =>
                {
                    root[DepositLocationKey] = _serialization.WriteValue(location, alwaysWrite: true);
                    return 1;
                });
                if (changes == 0)
                    throw new InvalidDataException("Could not write the deposit box position.");
                entityDataList.Add(snapshot);
            }
            catch (Exception ex)
            {
                Log.Error($"Failed to serialize item {ToPrettyString(item)} in safety deposit box: {ex}");
                ConsolePopup(player, Loc.GetString("safety-deposit-item-save-failed", ("item", Name(item))));
                PlayDenySound(consoleUid, component);
                return;
            }
        }

        Log.Info($"Saving {entityDataList.Count} items to database for box {boxComp.BoxId}");

        // Get nickname from label if it exists
        string? nickname = null;
        if (TryComp<LabelComponent>(boxEntity, out var boxLabel) && !string.IsNullOrEmpty(boxLabel.CurrentLabel))
        {
            nickname = boxLabel.CurrentLabel;
            Log.Info($"Saving box nickname: {nickname}");
        }

        // Update nickname if one was set
        if (nickname != null)
        {
            await _dbManager.UpdateSafetyDepositBoxNickname(boxComp.BoxId!.Value, nickname);
        }

        // Only replace the saved contents after every item has serialized successfully.
        await _dbManager.DepositSafetyDepositBoxItems(boxComp.BoxId!.Value, entityDataList);

        var itemCount = entityDataList.Count;
        _depositingBoxes.Remove(boxEntity);

        // Remove from slot before deleting to properly update UI
        if (Exists(consoleUid) && !TerminatingOrDeleted(consoleUid))
            _itemSlots.TryEject(consoleUid, component.BoxSlot, null, out _);

        // Delete the physical box
        Del(boxEntity);

        if (Exists(player) && !TerminatingOrDeleted(player))
            ConsolePopup(player, "Safety deposit box contents saved. The box has been stored.");
        if (Exists(consoleUid) && !TerminatingOrDeleted(consoleUid))
            PlayConfirmSound(consoleUid, component);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):actor} deposited safety deposit box {boxComp.BoxId} with {itemCount} items");
    }

    private async void OnWithdraw(EntityUid uid, SafetyDepositConsoleComponent component, SafetyDepositWithdrawMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!TryComp<ActorComponent>(player, out var actor))
            return;

        var userId = actor.PlayerSession.UserId;
        if (!_prefsManager.TryGetCachedPreferences(userId, out var prefs))
            return;

        var characterIndex = prefs.SelectedCharacterIndex;

        if (!_boxOperations.Add(args.BoxId))
            return;
        UpdateUI(uid, component, player);
        try
        {
            await WithdrawBoxAsync(uid, component, player, userId.UserId, characterIndex, args.BoxId);
        }
        catch (Exception exception)
        {
            Log.Error($"Failed withdrawing safety deposit box {args.BoxId}: {exception}");
            if (Exists(player))
                ConsolePopup(player, Loc.GetString("safety-deposit-load-failed"));
        }
        finally
        {
            _boxOperations.Remove(args.BoxId);
            if (Exists(uid) && Exists(player))
                UpdateUI(uid, component, player);
        }
    }

    private async void OnReclaim(EntityUid uid, SafetyDepositConsoleComponent component, SafetyDepositReclaimMessage args)
    {
        if (args.Actor is not { Valid: true } player)
            return;

        if (!TryComp<ActorComponent>(player, out var actor))
            return;

        var userId = actor.PlayerSession.UserId;
        if (!_prefsManager.TryGetCachedPreferences(userId, out var prefs))
            return;

        var characterIndex = prefs.SelectedCharacterIndex;

        if (!_boxOperations.Add(args.BoxId))
            return;
        UpdateUI(uid, component, player);
        try
        {
            await ReclaimBoxAsync(uid, component, player, userId.UserId, characterIndex, args.BoxId);
        }
        catch (Exception exception)
        {
            Log.Error($"Failed reclaiming safety deposit box {args.BoxId}: {exception}");
        }
        finally
        {
            _boxOperations.Remove(args.BoxId);
            if (Exists(uid) && Exists(player))
                UpdateUI(uid, component, player);
        }
    }

    private async Task ReclaimBoxAsync(
        EntityUid consoleUid,
        SafetyDepositConsoleComponent component,
        EntityUid player,
        Guid userId,
        int characterIndex,
        Guid boxId)
    {
        // Get box from database
        var box = await _dbManager.GetSafetyDepositBox(boxId);

        if (box == null)
        {
            ConsolePopup(player, "Box not found.");
            PlayDenySound(consoleUid, component);
            return;
        }

        // Verify ownership
        if (box.OwnerUserId != userId || box.CharacterIndex != characterIndex)
        {
            ConsolePopup(player, "This box does not belong to you.");
            PlayDenySound(consoleUid, component);
            return;
        }

        // Verify box is actually lost (withdrawn in previous round with no items)
        bool isLost = box.LastWithdrawn.HasValue &&
                      box.LastWithdrawnRoundId.HasValue &&
                      box.LastWithdrawnRoundId.Value != _gameTicker.RoundId &&
                      box.Items.Count == 0;

        if (!isLost)
        {
            ConsolePopup(player, "This box is not lost and cannot be reclaimed.");
            PlayDenySound(consoleUid, component);
            return;
        }

        // Replace only the physical container. The character's database slot and career binding stay unchanged.
        var newBox = box;

        // Spawn a new empty physical box
        string prototypeId = box.ProtoId;

        var boxEntity = Spawn(prototypeId, Transform(player).Coordinates);
        var boxComp = EnsureComp<SafetyDepositBoxComponent>(boxEntity);
        boxComp.BoxId = newBox.BoxId;
        boxComp.OwnerId = userId;
        boxComp.CharacterIndex = characterIndex;
        boxComp.OwnerName = MetaData(player).EntityName;
        Dirty(boxEntity, boxComp);

        // Mark the box as withdrawn in the current round (since we're giving them a physical box)
        await _dbManager.ClearSafetyDepositBoxItems(newBox.BoxId, _gameTicker.RoundId);

        // Restore nickname if one was saved
        if (!string.IsNullOrEmpty(box.Nickname))
        {
            _label.Label(boxEntity, box.Nickname);
        }

        // Try to put it in player's hands
        if (!_hands.TryPickupAnyHand(player, boxEntity))
        {
            _transform.SetLocalRotation(boxEntity, Angle.Zero);
        }

        ConsolePopup(player, "Lost box reclaimed! A new empty box has been issued.");
        PlayConfirmSound(consoleUid, component);

        _adminLogger.Add(LogType.Action, LogImpact.Medium,
            $"{ToPrettyString(player):actor} reclaimed lost safety deposit box {boxId}");
    }

    private async Task WithdrawBoxAsync(
        EntityUid consoleUid,
        SafetyDepositConsoleComponent component,
        EntityUid player,
        Guid userId,
        int characterIndex,
        Guid boxId)
    {
        // Get box from database
        var box = await _dbManager.GetSafetyDepositBox(boxId);

        if (box == null)
        {
            ConsolePopup(player, "Box not found.");
            PlayDenySound(consoleUid, component);
            return;
        }

        Log.Info($"WithdrawBoxAsync: Retrieved box {boxId} with {box.Items.Count} items from database");

        // Verify ownership
        if (box.OwnerUserId != userId || box.CharacterIndex != characterIndex)
        {
            ConsolePopup(player, "This box does not belong to you.");
            PlayDenySound(consoleUid, component);
            return;
        }

        // A stale enabled button is not authorization to issue an already withdrawn container again.
        if (box.LastWithdrawn.HasValue &&
            (box.LastWithdrawnRoundId != _gameTicker.RoundId || box.Items.Count == 0))
        {
            ConsolePopup(player, Loc.GetString("safety-deposit-console-box-unavailable"));
            return;
        }
        if (!Exists(player) || TerminatingOrDeleted(player))
            return;

        // Stage all items before committing the withdrawal or exposing anything to the player.
        var deliveryCoordinates = Transform(player).Coordinates;
        var boxEntity = Spawn(box.ProtoId);
        var restoredItems = new List<EntityUid>();
        var issued = false;
        try
        {
            var boxComp = EnsureComp<SafetyDepositBoxComponent>(boxEntity);
            boxComp.BoxId = box.BoxId;
            boxComp.OwnerId = userId;
            boxComp.CharacterIndex = characterIndex;
            // Use current character name instead of stored name in case they changed it
            boxComp.OwnerName = MetaData(player).EntityName;
            Dirty(boxEntity, boxComp);

            // Restore nickname if one was saved
            if (!string.IsNullOrEmpty(box.Nickname))
            {
                _label.Label(boxEntity, box.Nickname);
                Log.Info($"Restored box nickname: {box.Nickname}");
            }

            // Deserialize and spawn items into the box
            if (!TryComp<StorageComponent>(boxEntity, out var storageComp))
                throw new InvalidDataException($"Box entity {boxEntity} has no StorageComponent.");

            var items = new List<(EntityUid Item, ItemStorageLocation? Location)>();
            foreach (var itemData in box.Items)
            {
                ItemStorageLocation? location = null;
                using var reader = new StringReader(ShipSaveYamlSanitizer.ScrubStoredItemYaml(itemData.EntityData, out _, root =>
                {
                    if (root.TryGet(DepositLocationKey, out MappingDataNode? savedLocation))
                        location = _serialization.Read<ItemStorageLocation>(savedLocation);
                    return 0;
                }));
                if (!_loader.TryLoadEntity(reader, "safety deposit box", out var entity))
                    throw new InvalidDataException($"Failed to load a saved item from safety deposit box {boxId}.");

                var itemEntity = entity.Value.Owner;
                restoredItems.Add(itemEntity);
                IdentityDocumentPersistence.RemoveDocuments(EntityManager, itemEntity);
                if (Deleted(itemEntity))
                    continue;
                // Mark item as having been stored in a deposit box
                EnsureComp<SafetyDepositStoredComponent>(itemEntity);

                items.Add((itemEntity, location));
            }

            var overflow = RestoreDepositLayout((boxEntity, storageComp), items);
            await _dbManager.ClearSafetyDepositBoxItems(boxId, _gameTicker.RoundId);

            // The player may have disconnected while the database operation completed.
            _transform.SetCoordinates(boxEntity, deliveryCoordinates);
            foreach (var item in overflow)
                _transform.SetCoordinates(item, deliveryCoordinates);
            issued = true;
            if (Exists(player) && !TerminatingOrDeleted(player))
            {
                if (!_hands.TryPickupAnyHand(player, boxEntity))
                    _transform.SetLocalRotation(boxEntity, Angle.Zero);
                ConsolePopup(player, overflow.Count == 0
                    ? "Safety deposit box retrieved."
                    : Loc.GetString("safety-deposit-withdraw-overflow", ("count", overflow.Count)));
            }

            if (Exists(consoleUid))
                PlayConfirmSound(consoleUid, component);

            _adminLogger.Add(LogType.Action, LogImpact.Medium,
                $"{ToPrettyString(player):actor} withdrew safety deposit box {boxId} with {box.Items.Count} items ({overflow.Count} placed on the floor)");
        }
        finally
        {
            if (!issued)
            {
                // A failed restoration must leave the only saved copy in the database.
                foreach (var item in restoredItems)
                {
                    if (Exists(item) && !TerminatingOrDeleted(item))
                        Del(item);
                }
                if (Exists(boxEntity) && !TerminatingOrDeleted(boxEntity))
                    Del(boxEntity);
            }
        }
    }

    private void OnSlotChanged(EntityUid uid, SafetyDepositConsoleComponent component, ContainerModifiedMessage args)
    {
        // Update UI for anyone who has this console's UI open
        foreach (var actor in _uiSystem.GetActors(uid, SafetyDepositConsoleUiKey.Key))
        {
            UpdateUI(uid, component, actor);
        }
    }

    private void PlayDenySound(EntityUid uid, SafetyDepositConsoleComponent component)
    {
        _audio.PlayPvs(component.ErrorSound, uid);
    }

    private void PlayConfirmSound(EntityUid uid, SafetyDepositConsoleComponent component)
    {
        _audio.PlayPvs(component.ConfirmSound, uid);
    }

    private void ConsolePopup(EntityUid actor, string text)
    {
        _popup.PopupEntity(text, actor, actor);
    }
}

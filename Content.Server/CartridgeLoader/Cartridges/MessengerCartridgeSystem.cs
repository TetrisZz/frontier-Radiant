using Content.Server.Access.Systems;
using Content.Server.Administration.Logs;
using Content.Shared.Access.Components;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Content.Shared.Database;
using Content.Shared.Ghost;
using Content.Shared.Inventory;
using Content.Shared.PDA;
using Robust.Server.Player;
using Robust.Shared.Localization;
using Robust.Shared.Player;
using Robust.Shared.Timing;
using System.Linq;

namespace Content.Server.CartridgeLoader.Cartridges;

/// <summary>
/// Server-authoritative contact book and message store for PDA messengers.
/// A contact request must be accepted by its recipient before either PDA can send a message.
/// </summary>
public sealed class MessengerCartridgeSystem : EntitySystem
{
    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly IdCardSystem _idCards = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly IAdminLogManager _adminLogger = default!;

    private readonly Dictionary<int, MessengerUser> _users = new();
    private readonly HashSet<int> _activeUsers = new();
    private readonly Dictionary<EntityUid, int> _loaderAccounts = new();
    private readonly Dictionary<int, HashSet<int>> _contacts = new();
    private readonly Dictionary<int, HashSet<int>> _requests = new();
    private readonly List<MessengerMessageEntry> _messages = new();
    private readonly Dictionary<int, MessengerGroup> _groups = new();
    private int _nextGroupId = 1;
    private readonly HashSet<int> _notificationsMuted = new();
    private TimeSpan _nextStateRefresh;

    public override void Initialize()
    {
        SubscribeLocalEvent<MessengerCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<MessengerCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Keep open messenger windows in sync even when nobody presses Refresh.
        if (_timing.CurTime < _nextStateRefresh)
            return;

        _nextStateRefresh = _timing.CurTime + TimeSpan.FromSeconds(1);
        RefreshOpenMessengers();
    }

    private void OnUiReady(EntityUid uid, MessengerCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        // Radiant Sector: use the standard cartridge event signature so initial PDA state is never skipped.
        RefreshKnownUsers();
        UpdateUiState(args.Loader);
        RefreshOpenMessengers();
    }

    private void OnUiMessage(EntityUid uid, MessengerCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not MessengerUiMessageEvent message || !args.Actor.Valid)
            return;

        var loader = GetEntity(args.LoaderUid);
        RefreshKnownUsers();
        if (!TryGetUser(loader, out var user) && !TryGetUserFromActor(loader, args.Actor, out user))
            return;

        switch (message.Action)
        {
            case MessengerUiAction.RequestContact:
                RequestContact(user.Id, message.TargetId);
                break;
            case MessengerUiAction.AcceptContact:
                AcceptContact(user.Id, message.TargetId);
                break;
            case MessengerUiAction.DeclineContact:
                DeclineContact(user.Id, message.TargetId);
                break;
            case MessengerUiAction.SendMessage:
                SendMessage(user.Id, message.TargetId, message.Text, args.Actor);
                break;
            case MessengerUiAction.ReadChat:
                MarkRead(user.Id, message.TargetId);
                break;
            case MessengerUiAction.Refresh:
                break;
            case MessengerUiAction.ToggleNotifications:
                if (!_notificationsMuted.Add(user.Id))
                    _notificationsMuted.Remove(user.Id);
                break;
            case MessengerUiAction.RemoveContact:
                RemoveContact(user.Id, message.TargetId);
                break;
            case MessengerUiAction.CreateGroup:
                CreateGroup(user.Id, message.Text, message.Members);
                break;
            case MessengerUiAction.SelectProfilePhoto:
                SelectProfilePhoto(loader, user.Id, message.TargetId);
                break;
            case MessengerUiAction.CaptureProfilePhoto:
                CaptureProfilePhoto(loader, user.Id);
                break;
            case MessengerUiAction.RemoveProfilePhoto:
                RemoveProfilePhoto(user.Id);
                break;
            case MessengerUiAction.SendPhoto:
                SendStoredPhoto(loader, user.Id, message.TargetId, message.PhotoIndex, args.Actor);
                break;
            case MessengerUiAction.CaptureChatPhoto:
                CaptureChatPhoto(loader, user.Id, message.TargetId);
                break;
        }

        // Update both participants immediately; periodic refresh keeps idle windows current afterwards.
        RefreshOpenMessengers();
    }

    private bool TryGetUser(EntityUid loader, out MessengerUser user)
    {
        user = default!;
        if (!TryComp(loader, out PdaComponent? pda))
        {
            if (_loaderAccounts.TryGetValue(loader, out var accountId) && _users.TryGetValue(accountId, out var storedUser))
            {
                user = storedUser;
                return true;
            }

            return false;
        }

        if (pda.ContainedId is { } idCardUid && TryComp(idCardUid, out IdCardComponent? idCard))
        {
            user = RegisterUser(idCardUid, idCard.FullName ?? pda.OwnerName ?? Loc.GetString("generic-unknown"), idCard.LocalizedJobTitle ?? string.Empty);
            _loaderAccounts[loader] = user.Id;
            return true;
        }

        if (_loaderAccounts.TryGetValue(loader, out var fallbackId) && _users.TryGetValue(fallbackId, out var fallbackUser))
        {
            user = fallbackUser;
            return true;
        }

        return false;
    }

    /// <summary>Radiant Sector: Frontier PDAs may not have an inserted ID card, so use the active character as a fallback account.</summary>
    private bool TryGetUserFromActor(EntityUid loader, EntityUid actor, out MessengerUser user)
    {
        user = RegisterPlayer(actor) ?? RegisterUser(actor, Name(actor), string.Empty);
        _loaderAccounts[loader] = user.Id;
        return true;
    }

    /// <summary>Builds the directory from actual connected players, without station or PDA dependencies.</summary>
    private void RefreshKnownUsers()
    {
        _activeUsers.Clear();
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } player ||
                HasComp<GhostComponent>(player))
                continue;

            _activeUsers.Add(RegisterPlayer(player)!.Id);
        }
    }

    private MessengerUser? RegisterPlayer(EntityUid player)
    {
        // A messenger account is the physical ID card, not the humanoid entity.
        // Thus anyone holding a PDA with this ID sees the same conversations.
        if (!TryFindPlayerIdCard(player, out var idCard))
            return RegisterUser(player, Name(player), string.Empty); // Radiant Sector: show every spawned player, even without a physical ID.

        var accountId = idCard.Owner.Id;
        RemoveStaleAccount(player.Id, accountId);

        var name = string.IsNullOrWhiteSpace(idCard.Comp.FullName)
            ? Name(player)
            : idCard.Comp.FullName;
        var jobTitle = idCard.Comp.LocalizedJobTitle ?? string.Empty;
        return RegisterUser(idCard.Owner, name, jobTitle);
    }

    private bool TryFindPlayerIdCard(EntityUid player, out Entity<IdCardComponent> idCard)
    {
        if (_idCards.TryFindIdCard(player, out idCard))
            return true;

        // Frontier characters can carry a PDA in belt or pocket slots rather than in the ID slot.
        // Check every equipped item, including a PDA that contains the actual ID card.
        var slots = _inventory.GetSlotEnumerator(player);
        while (slots.NextItem(out var item))
        {
            if (_idCards.TryGetIdCard(item, out idCard))
                return true;
        }

        idCard = default;
        return false;
    }

    private MessengerUser RegisterUser(EntityUid owner, string name, string jobTitle)
    {
        if (_users.TryGetValue(owner.Id, out var existing))
        {
            var updated = existing with
            {
                Name = name,
                JobTitle = string.IsNullOrEmpty(jobTitle) ? existing.JobTitle : jobTitle,
            };
            _users[owner.Id] = updated;
            return updated;
        }

        var user = new MessengerUser(owner.Id, name, jobTitle, null);
        _users[user.Id] = user;
        return user;
    }

    /// <summary>Player-entity fallbacks are replaced once their ID card is discovered.</summary>
    private void RemoveStaleAccount(int staleId, int canonicalId)
    {
        if (staleId == canonicalId || !_users.Remove(staleId))
            return;

        if (_contacts.Remove(staleId, out var contacts))
        {
            if (!_contacts.TryGetValue(canonicalId, out var canonicalContacts))
                _contacts[canonicalId] = contacts;
            else
                canonicalContacts.UnionWith(contacts);
        }

        if (_requests.Remove(staleId, out var requests))
        {
            if (!_requests.TryGetValue(canonicalId, out var canonicalRequests))
                _requests[canonicalId] = requests;
            else
                canonicalRequests.UnionWith(requests);
        }

        foreach (var targetRequests in _requests.Values)
        {
            if (targetRequests.Remove(staleId))
                targetRequests.Add(canonicalId);
        }

        foreach (var ownerContacts in _contacts.Values)
        {
            if (ownerContacts.Remove(staleId))
                ownerContacts.Add(canonicalId);
        }

        for (var i = 0; i < _messages.Count; i++)
        {
            var message = _messages[i];
            var senderId = message.SenderId == staleId ? canonicalId : message.SenderId;
            var receiverId = message.ReceiverId == staleId ? canonicalId : message.ReceiverId;
            if (senderId == message.SenderId && receiverId == message.ReceiverId)
                continue;

            _messages[i] = new MessengerMessageEntry(
                senderId,
                receiverId,
                message.SenderName,
                message.Content,
                message.Timestamp,
                message.GroupId,
                message.ImageData);
        }

        foreach (var group in _groups.Values)
        {
            if (group.Members.Remove(staleId))
                group.Members.Add(canonicalId);
        }

        foreach (var (loader, accountId) in _loaderAccounts.ToList())
        {
            if (accountId == staleId)
                _loaderAccounts[loader] = canonicalId;
        }

        foreach (var key in _lastRead.Keys.ToList())
        {
            var (reader, partner) = key;
            var newReader = reader == staleId ? canonicalId : reader;
            var newPartner = partner == staleId ? canonicalId : partner;
            if (newReader == reader && newPartner == partner)
                continue;

            var time = _lastRead[key];
            _lastRead.Remove(key);
            _lastRead[(newReader, newPartner)] = time;
        }

        if (_notificationsMuted.Remove(staleId))
            _notificationsMuted.Add(canonicalId);
    }

    private HashSet<int> GetSupersededAccountIds()
    {
        var superseded = new HashSet<int>();
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { Valid: true } player)
                continue;

            if (TryFindPlayerIdCard(player, out var idCard) && player.Id != idCard.Owner.Id)
                superseded.Add(player.Id);
        }

        return superseded;
    }

    private void RequestContact(int senderId, int targetId)
    {
        if (senderId == targetId || !_users.ContainsKey(targetId) || AreContacts(senderId, targetId))
            return;

        if (!_requests.TryGetValue(targetId, out var requests))
            _requests[targetId] = requests = new HashSet<int>();
        requests.Add(senderId);

        if (!_notificationsMuted.Contains(targetId) && FindPdaForIdCard(targetId) is { } receiverPda)
            _cartridgeLoader.SendNotification(receiverPda, Loc.GetString("messenger-contact-request-title"), Loc.GetString("messenger-contact-request-message", ("name", _users[senderId].Name)));
    }

    private void AcceptContact(int recipientId, int senderId)
    {
        if (!_requests.TryGetValue(recipientId, out var requests) || !requests.Remove(senderId))
            return;

        AddContact(recipientId, senderId);
        AddContact(senderId, recipientId);

        // Radiant Sector: notify the requester when their friend request is accepted.
        if (!_notificationsMuted.Contains(senderId) && FindPdaForIdCard(senderId) is { } senderPda)
        {
            _cartridgeLoader.SendNotification(
                senderPda,
                Loc.GetString("messenger-contact-accepted-title"),
                Loc.GetString("messenger-contact-accepted-message", ("name", _users[recipientId].Name)));
        }
    }

    private void DeclineContact(int recipientId, int senderId)
    {
        if (_requests.TryGetValue(recipientId, out var requests))
            requests.Remove(senderId);
    }

    private void SendMessage(int senderId, int receiverId, string? text, EntityUid senderActor, byte[]? imageData = null)
    {
        var content = text?.Trim();
        if ((string.IsNullOrEmpty(content) && imageData == null) || content?.Length > 512)
            return;

        if (receiverId < 0)
        {
            SendGroupMessage(senderId, -receiverId, content ?? string.Empty, senderActor, imageData);
            return;
        }

        if (!AreContacts(senderId, receiverId))
            return;

        _messages.Add(new MessengerMessageEntry(senderId, receiverId, _users[senderId].Name, content ?? string.Empty, _timing.CurTime, imageData: imageData));
        if (_messages.Count > 500)
            _messages.RemoveRange(0, _messages.Count - 500);

        var sender = _users[senderId];
        var receiver = _users[receiverId];
        _adminLogger.Add(LogType.Chat, LogImpact.Low,
            $"Messenger message from {ToPrettyString(senderActor):user} ({sender.Name}) to {receiver.Name}: {content ?? "[photo]"}");

        if (!_notificationsMuted.Contains(receiverId) && FindPdaForIdCard(receiverId) is { } receiverPda)
        {
            _cartridgeLoader.SendNotification(
                receiverPda,
                Loc.GetString("messenger-notification-title"),
                Loc.GetString("messenger-notification-message"));
        }
    }

    private void CreateGroup(int ownerId, string? title, List<int>? members)
    {
        var name = title?.Trim();
        if (string.IsNullOrWhiteSpace(name) || name.Length > 48 || members == null)
            return;

        var groupMembers = members.Where(id => id != ownerId && AreContacts(ownerId, id)).Distinct().Take(15).ToHashSet();
        if (groupMembers.Count == 0)
            return;

        groupMembers.Add(ownerId);
        _groups[_nextGroupId] = new MessengerGroup(_nextGroupId, name, groupMembers);
        _nextGroupId++;
    }

    private void SendGroupMessage(int senderId, int groupId, string content, EntityUid senderActor, byte[]? imageData = null)
    {
        if (!_groups.TryGetValue(groupId, out var group) || !group.Members.Contains(senderId))
            return;

        _messages.Add(new MessengerMessageEntry(senderId, -groupId, _users[senderId].Name, content, _timing.CurTime, groupId, imageData));
        if (_messages.Count > 500)
            _messages.RemoveRange(0, _messages.Count - 500);

        var sender = _users[senderId];
        var recipients = string.Join(", ", group.Members
            .Where(member => member != senderId && _users.ContainsKey(member))
            .Select(member => _users[member].Name));
        _adminLogger.Add(LogType.Chat, LogImpact.Low,
            $"Messenger group message from {ToPrettyString(senderActor):user} ({sender.Name}) to '{group.Name}' ({recipients}): {content}{(imageData == null ? string.Empty : " [photo]")}");

        foreach (var member in group.Members.Where(member => member != senderId))
        {
            if (!_notificationsMuted.Contains(member) && FindPdaForIdCard(member) is { } receiverPda)
                _cartridgeLoader.SendNotification(receiverPda, Loc.GetString("messenger-notification-title"), Loc.GetString("messenger-notification-message"));
        }
    }

    private void MarkRead(int readerId, int partnerId)
    {
        // Unread counts are calculated from this read marker, avoiding client-side trust.
        _lastRead[(readerId, partnerId)] = _timing.CurTime;
    }

    private readonly Dictionary<(int Reader, int Partner), TimeSpan> _lastRead = new();

    private EntityUid? FindPdaForIdCard(int idCardId)
    {
        var query = EntityQueryEnumerator<PdaComponent, CartridgeLoaderComponent>();
        while (query.MoveNext(out var pdaUid, out var pda, out _))
        {
            if (pda.ContainedId?.Id == idCardId)
                return pdaUid;
        }

        return null;
    }

    private void RefreshOpenMessengers()
    {
        var query = EntityQueryEnumerator<CartridgeLoaderComponent>();
        while (query.MoveNext(out var loaderUid, out var loader))
        {
            if (loader.ActiveProgram is not { } program || !HasComp<MessengerCartridgeComponent>(program))
                continue;

            UpdateUiState(loaderUid);
        }
    }

    /// <summary>Radiant Sector: assigns a saved PDA-camera image as the account profile photo.</summary>
    public void SetProfilePhoto(int accountId, byte[] imageData)
    {
        if (!_users.TryGetValue(accountId, out var user))
            return;

        _users[accountId] = user with { ProfilePhoto = imageData };
        RefreshOpenMessengers();
    }

    private void RemoveProfilePhoto(int accountId)
    {
        if (!_users.TryGetValue(accountId, out var user))
            return;

        // Radiant Sector: removing an avatar only clears the profile selection, never the PDA gallery.
        _users[accountId] = user with { ProfilePhoto = null };
        RefreshOpenMessengers();
    }

    private void SelectProfilePhoto(EntityUid loader, int accountId, int photoIndex)
    {
        if (!_cartridgeLoader.TryGetProgram<PdaCameraCartridgeComponent>(loader, out _, out var camera) ||
            photoIndex < 0 || photoIndex >= camera.Photos.Count)
            return;

        SetProfilePhoto(accountId, camera.Photos[photoIndex]);
    }

    private void CaptureProfilePhoto(EntityUid loader, int accountId)
    {
        if (!_cartridgeLoader.TryGetProgram<PdaCameraCartridgeComponent>(loader, out var cameraUid, out var camera))
            return;

        camera.PendingProfileAccountId = accountId;
        _cartridgeLoader.ActivateProgram(loader, cameraUid.Value);
    }

    /// <summary>Radiant Sector: sends a selected photo from the current PDA's internal camera library.</summary>
    private void SendStoredPhoto(EntityUid loader, int senderId, int targetId, int photoIndex, EntityUid senderActor)
    {
        if (!_cartridgeLoader.TryGetProgram<PdaCameraCartridgeComponent>(loader, out _, out var camera) ||
            photoIndex < 0 || photoIndex >= camera.Photos.Count)
            return;

        SendPhoto(senderId, targetId, senderActor, camera.Photos[photoIndex]);
    }

    /// <summary>Radiant Sector: validates and stores a photo message through the normal contact/group restrictions.</summary>
    public void SendPhoto(int senderId, int targetId, EntityUid senderActor, byte[] imageData)
    {
        SendMessage(senderId, targetId, null, senderActor, imageData);
    }

    private void CaptureChatPhoto(EntityUid loader, int senderId, int targetId)
    {
        if (!_cartridgeLoader.TryGetProgram<PdaCameraCartridgeComponent>(loader, out var cameraUid, out var camera))
            return;

        camera.PendingMessageSenderId = senderId;
        camera.PendingMessageTargetId = targetId;
        _cartridgeLoader.ActivateProgram(loader, cameraUid.Value);
    }

    private void UpdateUiState(EntityUid loader, MessengerUser? currentUser = null)
    {
        RefreshKnownUsers();
        if (currentUser == null && !TryGetUser(loader, out currentUser))
            // The first UI-ready event has no actor. Still show the player directory;
            // subsequent button presses use the actor supplied by the UI relay.
            currentUser = new MessengerUser(loader.Id, string.Empty, string.Empty, null);

        var user = currentUser!;

        var contacts = _contacts.GetValueOrDefault(user.Id, [])
            .Where(id => _users.ContainsKey(id) && _activeUsers.Contains(id))
            .Select(id => ToEntry(user.Id, _users[id]))
            .OrderByDescending(entry => entry.UnreadCount)
            .ThenBy(entry => entry.Name)
            .ToList();
        var supersededAccounts = GetSupersededAccountIds();
        var available = _users.Values
            .Where(candidate => candidate.Id != user.Id && _activeUsers.Contains(candidate.Id) && !supersededAccounts.Contains(candidate.Id))
            .Where(candidate => !AreContacts(user.Id, candidate.Id))
            .Where(candidate => !_requests.GetValueOrDefault(candidate.Id, []).Contains(user.Id))
            .Select(candidate => ToEntry(user.Id, candidate))
            .OrderBy(entry => entry.Name)
            .ToList();
        var requests = _requests.GetValueOrDefault(user.Id, [])
            .Where(id => _users.ContainsKey(id) && _activeUsers.Contains(id))
            .Select(id => ToEntry(user.Id, _users[id]))
            .OrderBy(entry => entry.Name)
            .ToList();
        var groups = _groups.Values.Where(group => group.Members.Contains(user.Id)).Select(group => new MessengerGroupEntry(
            group.Id,
            group.Name,
            _messages.Count(message => message.GroupId == group.Id && message.SenderId != user.Id && message.Timestamp > _lastRead.GetValueOrDefault((user.Id, -group.Id), TimeSpan.Zero)))).ToList();
        var groupIds = groups.Select(group => group.Id).ToHashSet();
        var messages = _messages.Where(message => message.SenderId == user.Id || message.ReceiverId == user.Id || groupIds.Contains(message.GroupId)).ToList();
        var cameraPhotos = _cartridgeLoader.TryGetProgram<PdaCameraCartridgeComponent>(loader, out _, out var camera)
            ? camera.Photos.ToList()
            : [];

        _cartridgeLoader.UpdateCartridgeUiState(loader, new MessengerUiState(
            user.Id,
            contacts,
            available,
            requests,
            messages,
            groups,
            !_notificationsMuted.Contains(user.Id),
            cameraPhotos.Count,
            cameraPhotos));
    }

    /// <summary>Radiant Sector: refreshes the just-activated messenger after a camera action completes.</summary>
    public void RefreshUi(EntityUid loader, int accountId)
    {
        if (_users.TryGetValue(accountId, out var user))
            UpdateUiState(loader, user);
    }

    private MessengerContactEntry ToEntry(int ownerId, MessengerUser user)
    {
        var readTime = _lastRead.GetValueOrDefault((ownerId, user.Id), TimeSpan.Zero);
        var unread = _messages.Count(message => message.SenderId == user.Id && message.ReceiverId == ownerId && message.Timestamp > readTime);
        return new MessengerContactEntry(user.Id, user.Name, user.JobTitle, unread, user.ProfilePhoto);
    }

    private bool AreContacts(int first, int second) => _contacts.GetValueOrDefault(first, []).Contains(second);

    private void AddContact(int owner, int contact)
    {
        if (!_contacts.TryGetValue(owner, out var contacts))
            _contacts[owner] = contacts = new HashSet<int>();
        contacts.Add(contact);
    }

    private void RemoveContact(int owner, int contact)
    {
        var removed = _contacts.TryGetValue(owner, out var ownerContacts) && ownerContacts.Remove(contact);
        if (_contacts.TryGetValue(contact, out var contactContacts))
            contactContacts.Remove(owner);

        // Radiant Sector: notify the other participant about a real contact removal.
        if (!removed ||
            _notificationsMuted.Contains(contact) ||
            !_users.TryGetValue(owner, out var ownerUser) ||
            FindPdaForIdCard(contact) is not { } contactPda)
        {
            return;
        }

        _cartridgeLoader.SendNotification(
            contactPda,
            Loc.GetString("messenger-contact-removed-title"),
            Loc.GetString("messenger-contact-removed-message", ("name", ownerUser.Name)));
    }

    private sealed record MessengerUser(int Id, string Name, string JobTitle, byte[]? ProfilePhoto);
    private sealed record MessengerGroup(int Id, string Name, HashSet<int> Members);
}

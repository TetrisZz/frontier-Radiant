using Robust.Shared.Serialization;

namespace Content.Shared.CartridgeLoader.Cartridges;

/// <summary>State shown by the PDA messenger. Only confirmed contacts can receive messages.</summary>
[Serializable, NetSerializable]
public sealed class MessengerUiState : BoundUserInterfaceState
{
    /// <summary>ID card account currently viewing this state. Used to unambiguously match direct-message history.</summary>
    public int OwnerId;
    public List<MessengerContactEntry> Contacts;
    public List<MessengerContactEntry> AvailableUsers;
    public List<MessengerContactEntry> IncomingRequests;
    public List<MessengerMessageEntry> Messages;
    public List<MessengerGroupEntry> Groups;
    public bool NotificationsEnabled;
    public int CameraPhotoCount;
    // Radiant Sector: photo thumbnails shown by the messenger's PDA-camera gallery.
    public List<byte[]> CameraPhotos;

    public MessengerUiState(
        int ownerId,
        List<MessengerContactEntry> contacts,
        List<MessengerContactEntry> availableUsers,
        List<MessengerContactEntry> incomingRequests,
        List<MessengerMessageEntry> messages,
        List<MessengerGroupEntry> groups,
        bool notificationsEnabled,
        int cameraPhotoCount,
        List<byte[]> cameraPhotos)
    {
        OwnerId = ownerId;
        Contacts = contacts;
        AvailableUsers = availableUsers;
        IncomingRequests = incomingRequests;
        Messages = messages;
        Groups = groups;
        NotificationsEnabled = notificationsEnabled;
        CameraPhotoCount = cameraPhotoCount;
        CameraPhotos = cameraPhotos;
    }
}

[Serializable, NetSerializable]
public sealed class MessengerContactEntry
{
    public int Id;
    public string Name;
    public string JobTitle;
    public int UnreadCount;
    public byte[]? ProfilePhoto;

    public MessengerContactEntry(int id, string name, string jobTitle, int unreadCount = 0, byte[]? profilePhoto = null)
    {
        Id = id;
        Name = name;
        JobTitle = jobTitle;
        UnreadCount = unreadCount;
        ProfilePhoto = profilePhoto;
    }
}

[Serializable, NetSerializable]
public sealed class MessengerMessageEntry
{
    public int SenderId;
    public int ReceiverId;
    public string SenderName;
    public string Content;
    public TimeSpan Timestamp;
    public int GroupId;
    // Radiant Sector: optional PNG stored with a messenger image message.
    public byte[]? ImageData;

    public MessengerMessageEntry(int senderId, int receiverId, string senderName, string content, TimeSpan timestamp, int groupId = 0, byte[]? imageData = null)
    {
        SenderId = senderId;
        ReceiverId = receiverId;
        SenderName = senderName;
        Content = content;
        Timestamp = timestamp;
        GroupId = groupId;
        ImageData = imageData;
    }
}

[Serializable, NetSerializable]
public sealed class MessengerGroupEntry
{
    public int Id;
    public string Name;
    public int UnreadCount;

    public MessengerGroupEntry(int id, string name, int unreadCount = 0)
    {
        Id = id;
        Name = name;
        UnreadCount = unreadCount;
    }
}

using Robust.Shared.Serialization;

namespace Content.Shared.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class MessengerUiMessageEvent : CartridgeMessageEvent
{
    public MessengerUiAction Action;
    public int TargetId;
    public string? Text;
    public List<int>? Members;
    // Radiant Sector: index of a saved PDA-camera photo selected for chat delivery.
    public int PhotoIndex;

    public MessengerUiMessageEvent(MessengerUiAction action, int targetId = 0, string? text = null, List<int>? members = null, int photoIndex = -1)
    {
        Action = action;
        TargetId = targetId;
        Text = text;
        Members = members;
        PhotoIndex = photoIndex;
    }
}

[Serializable, NetSerializable]
public enum MessengerUiAction
{
    RequestContact,
    AcceptContact,
    DeclineContact,
    SendMessage,
    ReadChat,
    Refresh,
    ToggleNotifications,
    RemoveContact,
    CreateGroup,
    SelectProfilePhoto,
    CaptureProfilePhoto,
    // Radiant Sector: clears the active profile image without removing the stored camera photo.
    RemoveProfilePhoto,
    // Radiant Sector: sends a saved photo or starts a camera capture for the open chat.
    SendPhoto,
    CaptureChatPhoto,
}

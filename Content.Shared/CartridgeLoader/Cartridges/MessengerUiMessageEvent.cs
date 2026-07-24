using Robust.Shared.Serialization;

namespace Content.Shared.CartridgeLoader.Cartridges;

[Serializable, NetSerializable]
public sealed class MessengerUiMessageEvent : CartridgeMessageEvent
{
    public MessengerUiAction Action;
    public int TargetId;
    public string? Text;
    public List<int>? Members;

    public MessengerUiMessageEvent(MessengerUiAction action, int targetId = 0, string? text = null, List<int>? members = null)
    {
        Action = action;
        TargetId = targetId;
        Text = text;
        Members = members;
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
}

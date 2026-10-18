using Content.Shared.CartridgeLoader;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Messenger;

[Serializable, NetSerializable]
public sealed class MessengerUiState : BoundUserInterfaceState
{
    public readonly List<MessengerContact> Contacts;
    public readonly List<MessengerMessage> Messages;
    public readonly NetEntity? Selected;
    public readonly bool Muted;
    public readonly string? Error;

    public MessengerUiState(List<MessengerContact> contacts, List<MessengerMessage> messages,
        NetEntity? selected, bool muted, string? error)
    {
        Contacts = contacts;
        Messages = messages;
        Selected = selected;
        Muted = muted;
        Error = error;
    }
}

[Serializable, NetSerializable]
public sealed record MessengerContact(NetEntity Address, string Name, bool Available, int Unread, string Preview);

[Serializable, NetSerializable]
public sealed record MessengerMessage(NetEntity Peer, string PeerName, string Text, TimeSpan Time, bool Incoming);

[Serializable, NetSerializable]
public enum MessengerAction : byte
{
    Select,
    Send,
    ToggleMute,
}

[Serializable, NetSerializable]
public sealed class MessengerUiMessage : CartridgeMessageEvent
{
    public const int MaxLength = 500;
    public readonly MessengerAction Action;
    public readonly NetEntity? Target;
    public readonly string Text;

    public MessengerUiMessage(MessengerAction action, NetEntity? target = null, string text = "")
    {
        Action = action;
        Target = target;
        Text = text;
    }
}

using Content.Shared._radiant.Messenger;

namespace Content.Server._radiant.Messenger;

[RegisterComponent]
public sealed partial class MessengerCartridgeComponent : Component
{
    public readonly List<MessengerMessage> Messages = new();
    public readonly Dictionary<NetEntity, int> Unread = new();
    public NetEntity? Selected;
    public TimeSpan NextSend;
    public bool Muted;
    public string? Error;
}

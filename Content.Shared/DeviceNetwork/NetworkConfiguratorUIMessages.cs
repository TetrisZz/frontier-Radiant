using Robust.Shared.Serialization;

namespace Content.Shared.DeviceNetwork;

[Serializable, NetSerializable]
public enum NetworkConfiguratorUiKey
{
    List,
    Configure,
    Link
}

[Serializable, NetSerializable]
public enum NetworkConfiguratorButtonKey
{
    Set,
    Add,
    Edit,
    Clear,
    Copy,
    Show
}

/// <summary>
/// Message sent when the remove button for one device on the list was pressed
/// </summary>
[Serializable, NetSerializable]
public sealed class NetworkConfiguratorRemoveDeviceMessage : BoundUserInterfaceMessage
{
    public readonly string Address;

    public NetworkConfiguratorRemoveDeviceMessage(string address)
    {
        Address = address;
    }
}

/// <summary>
/// Message sent when the clear button was pressed
/// </summary>
[Serializable, NetSerializable]
public sealed class NetworkConfiguratorClearDevicesMessage : BoundUserInterfaceMessage
{
}

[Serializable, NetSerializable]
public sealed class NetworkConfiguratorButtonPressedMessage : BoundUserInterfaceMessage
{
    public readonly NetworkConfiguratorButtonKey ButtonKey;

    public NetworkConfiguratorButtonPressedMessage(NetworkConfiguratorButtonKey buttonKey)
    {
        ButtonKey = buttonKey;
    }
}

[Serializable, NetSerializable]
public sealed class NetworkConfiguratorClearLinksMessage : BoundUserInterfaceMessage
{
    public readonly uint LinkContext;

    public NetworkConfiguratorClearLinksMessage(uint linkContext = 0)
    {
        LinkContext = linkContext;
    }
}

[Serializable, NetSerializable]
public sealed class NetworkConfiguratorToggleLinkMessage : BoundUserInterfaceMessage
{
    public readonly string Source;
    public readonly string Sink;
    public readonly uint LinkContext;

    public NetworkConfiguratorToggleLinkMessage(string source, string sink, uint linkContext = 0)
    {
        Source = source;
        Sink = sink;
        LinkContext = linkContext;
    }
}

[Serializable, NetSerializable]
public sealed class NetworkConfiguratorLinkDefaultsMessage : BoundUserInterfaceMessage
{
    public readonly uint LinkContext;

    public NetworkConfiguratorLinkDefaultsMessage(uint linkContext)
    {
        LinkContext = linkContext;
    }
}

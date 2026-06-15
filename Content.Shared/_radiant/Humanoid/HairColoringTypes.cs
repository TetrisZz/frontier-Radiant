using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Humanoid;

[Serializable, NetSerializable]
public enum HairColoringMode : byte
{
    Solid,
    Gradient
}

/// <summary>
/// Ось градиента для окрашивания волос.
/// </summary>
[Serializable, NetSerializable]
public enum HairGradientDirection : byte
{
    LeftToRight,
    RightToLeft,
    TopToBottom,
    BottomToTop,
    TopLeftToBottomRight,
    BottomLeftToTopRight
}

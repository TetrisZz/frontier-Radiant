using Robust.Shared.Serialization;

namespace Content.Shared.CartridgeLoader.Cartridges;

/// <summary>
/// Radiant Sector: PDA camera program state and UI commands.
/// </summary>
[Serializable, NetSerializable]
public sealed class PdaCameraUiState(NetEntity loader, bool selfieMode, bool galleryOpen, List<byte[]> photos) : BoundUserInterfaceState
{
    public NetEntity Loader { get; } = loader;
    public bool SelfieMode { get; } = selfieMode;
    public bool GalleryOpen { get; } = galleryOpen;
    // Radiant Sector: the PDA camera owns these photos and exposes them only to its local gallery.
    public List<byte[]> Photos { get; } = photos;
}

[Serializable, NetSerializable]
public sealed class PdaCameraUiMessageEvent : CartridgeMessageEvent
{
    public PdaCameraUiAction Action;
    public byte[]? ImageData;
    // Radiant Sector: identifies a stored PDA photo for gallery-only actions.
    public int PhotoIndex;

    public PdaCameraUiMessageEvent(PdaCameraUiAction action, byte[]? imageData = null, int photoIndex = -1)
    {
        Action = action;
        ImageData = imageData;
        PhotoIndex = photoIndex;
    }
}

[Serializable, NetSerializable]
public enum PdaCameraUiAction : byte
{
    Capture,
    ToggleSelfie,
    ToggleGallery,
    DeletePhoto,
}

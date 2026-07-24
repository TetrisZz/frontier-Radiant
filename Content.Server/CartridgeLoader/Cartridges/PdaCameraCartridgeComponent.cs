namespace Content.Server.CartridgeLoader.Cartridges;

/// <summary>
/// Radiant Sector: stores the selected view mode, capture cooldown, and internal photo library for the PDA camera program.
/// </summary>
[RegisterComponent]
public sealed partial class PdaCameraCartridgeComponent : Component
{
    public bool SelfieMode;
    public bool GalleryOpen;
    public TimeSpan NextCapture;
    public readonly List<byte[]> Photos = new();
    public int? PendingProfileAccountId;
    // Radiant Sector: target retained while the camera is opened from a messenger chat.
    public int? PendingMessageSenderId;
    public int PendingMessageTargetId;
}

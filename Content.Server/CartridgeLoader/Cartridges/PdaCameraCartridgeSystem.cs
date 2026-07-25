using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Server.Audio;
using Robust.Shared.Audio;
using Robust.Shared.Timing;

namespace Content.Server.CartridgeLoader.Cartridges;

/// <summary>
/// Radiant Sector: server-authoritative capture and internal storage for the PDA camera program.
/// </summary>
public sealed class PdaCameraCartridgeSystem : EntitySystem
{
    private const int MaxImageSize = 1024 * 96;

    [Dependency] private readonly CartridgeLoaderSystem _cartridgeLoader = default!;
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MessengerCartridgeSystem _messenger = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<PdaCameraCartridgeComponent, CartridgeUiReadyEvent>(OnUiReady);
        SubscribeLocalEvent<PdaCameraCartridgeComponent, CartridgeMessageEvent>(OnUiMessage);
    }

    private void OnUiReady(EntityUid uid, PdaCameraCartridgeComponent component, CartridgeUiReadyEvent args)
    {
        UpdateUiState(uid, args.Loader, component);
    }

    private void OnUiMessage(EntityUid uid, PdaCameraCartridgeComponent component, CartridgeMessageEvent args)
    {
        if (args is not PdaCameraUiMessageEvent message || !args.Actor.Valid)
            return;

        switch (message.Action)
        {
            case PdaCameraUiAction.ToggleSelfie:
                component.SelfieMode = !component.SelfieMode;
                UpdateUiState(uid, GetEntity(args.LoaderUid), component);
                break;
            case PdaCameraUiAction.ToggleGallery:
                component.GalleryOpen = !component.GalleryOpen;
                UpdateUiState(uid, GetEntity(args.LoaderUid), component);
                break;
            case PdaCameraUiAction.DeletePhoto when message.PhotoIndex >= 0 && message.PhotoIndex < component.Photos.Count:
                // Radiant Sector: photos are owned by the PDA, so deletion must be server-authoritative.
                component.Photos.RemoveAt(message.PhotoIndex);
                UpdateUiState(uid, GetEntity(args.LoaderUid), component);
                break;
            case PdaCameraUiAction.Capture when message.ImageData != null:
                if (TryCapture(component, args.Actor, message.ImageData))
                {
                    UpdateUiState(uid, GetEntity(args.LoaderUid), component);
                    CompletePendingProfileCapture(component, GetEntity(args.LoaderUid), message.ImageData);
                    CompletePendingMessageCapture(component, GetEntity(args.LoaderUid), args.Actor, message.ImageData);
                }
                break;
        }
    }

    private bool TryCapture(PdaCameraCartridgeComponent component, EntityUid user, byte[] imageData)
    {
        if (imageData.Length > MaxImageSize || !HasPngSignature(imageData) || component.NextCapture > _timing.CurTime)
            return false;

        component.NextCapture = _timing.CurTime + TimeSpan.FromSeconds(1);
        // PDA photos remain on the program entity rather than spawning physical photo cards.
        component.Photos.Add(imageData);
        if (component.Photos.Count > 30)
            component.Photos.RemoveAt(0);

        _audio.PlayPvs(new SoundPathSpecifier("/Audio/_CorvaxGoob/Effects/photo_shoot.ogg"), user);
        return true;
    }

    private void UpdateUiState(EntityUid uid, EntityUid loader, PdaCameraCartridgeComponent component)
    {
        _cartridgeLoader.UpdateCartridgeUiState(loader, new PdaCameraUiState(
            GetNetEntity(loader),
            component.SelfieMode,
            component.GalleryOpen,
            new List<byte[]>(component.Photos)));
    }

    private void CompletePendingProfileCapture(PdaCameraCartridgeComponent component, EntityUid loader, byte[] imageData)
    {
        if (component.PendingProfileAccountId is not { } accountId)
            return;

        component.PendingProfileAccountId = null;
        _messenger.SetProfilePhoto(accountId, imageData);

        if (_cartridgeLoader.TryGetProgram<MessengerCartridgeComponent>(loader, out var messengerProgram))
        {
            _cartridgeLoader.ActivateProgram(loader, messengerProgram.Value);
            _messenger.RefreshUi(loader, accountId);
        }
    }

    private void CompletePendingMessageCapture(PdaCameraCartridgeComponent component, EntityUid loader, EntityUid actor, byte[] imageData)
    {
        if (component.PendingMessageSenderId is not { } senderId)
            return;

        var targetId = component.PendingMessageTargetId;
        component.PendingMessageSenderId = null;
        component.PendingMessageTargetId = 0;
        _messenger.SendPhoto(senderId, targetId, actor, imageData);

        if (_cartridgeLoader.TryGetProgram<MessengerCartridgeComponent>(loader, out var messengerProgram))
        {
            _cartridgeLoader.ActivateProgram(loader, messengerProgram.Value);
            _messenger.RefreshUi(loader, senderId);
        }
    }

    private static bool HasPngSignature(ReadOnlySpan<byte> data) =>
        data.Length >= 8 && data[0] == 0x89 && data[1] == 0x50 && data[2] == 0x4E && data[3] == 0x47 &&
        data[4] == 0x0D && data[5] == 0x0A && data[6] == 0x1A && data[7] == 0x0A;
}

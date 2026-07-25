using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Client.UserInterface;

namespace Content.Client.CartridgeLoader.Cartridges;

/// <summary>
/// Radiant Sector: connects the PDA camera fragment to its cartridge messages.
/// </summary>
public sealed partial class PdaCameraUi : UIFragment
{
    private PdaCameraUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new PdaCameraUiFragment();
        _fragment.OnToggleSelfie += () => userInterface.SendMessage(new CartridgeUiMessage(
            new PdaCameraUiMessageEvent(PdaCameraUiAction.ToggleSelfie)));
        _fragment.OnToggleGallery += () => userInterface.SendMessage(new CartridgeUiMessage(
            new PdaCameraUiMessageEvent(PdaCameraUiAction.ToggleGallery)));
        _fragment.OnDeletePhoto += index => userInterface.SendMessage(new CartridgeUiMessage(
            new PdaCameraUiMessageEvent(PdaCameraUiAction.DeletePhoto, photoIndex: index)));
        _fragment.OnCapture += () => _fragment.RenderImage(data => userInterface.SendMessage(new CartridgeUiMessage(
            new PdaCameraUiMessageEvent(PdaCameraUiAction.Capture, data))));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is PdaCameraUiState cameraState)
            _fragment?.UpdateState(cameraState);
    }
}

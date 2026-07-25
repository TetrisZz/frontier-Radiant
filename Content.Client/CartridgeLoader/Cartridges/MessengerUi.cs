using Content.Client.UserInterface.Fragments;
using Content.Shared.CartridgeLoader;
using Content.Shared.CartridgeLoader.Cartridges;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client.CartridgeLoader.Cartridges;

public sealed partial class MessengerUi : UIFragment
{
    private MessengerUiFragment? _fragment;

    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new MessengerUiFragment();
        _fragment.OnAction += (action, target, text, members) => userInterface.SendMessage(new CartridgeUiMessage(new MessengerUiMessageEvent(action, target, text, members)));
        // Radiant Sector: photo sends need an explicit library index in addition to the chat target.
        _fragment.OnPhotoAction += (action, target, photoIndex) => userInterface.SendMessage(new CartridgeUiMessage(new MessengerUiMessageEvent(action, target, photoIndex: photoIndex)));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is MessengerUiState messengerState)
            _fragment?.UpdateState(messengerState);
    }
}

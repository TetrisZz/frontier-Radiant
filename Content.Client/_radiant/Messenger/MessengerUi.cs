using Content.Client.UserInterface.Fragments;
using Content.Shared._radiant.Messenger;
using Content.Shared.CartridgeLoader;
using Robust.Client.GameObjects;
using Robust.Client.UserInterface;

namespace Content.Client._radiant.Messenger;

public sealed partial class MessengerUi : UIFragment
{
    private MessengerUiFragment? _fragment;
    public override Control GetUIFragmentRoot() => _fragment!;

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new MessengerUiFragment();
        _fragment.OnMessage += message => userInterface.SendMessage(new CartridgeUiMessage(message));
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is MessengerUiState messenger)
            _fragment?.UpdateState(messenger);
    }
}

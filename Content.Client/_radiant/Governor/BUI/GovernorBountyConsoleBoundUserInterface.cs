using Content.Client._radiant.Governor.UI;
using Content.Shared._radiant.Governor;
using Robust.Client.UserInterface;

namespace Content.Client._radiant.Governor.BUI;

// Bridge between the client window and the server.
public sealed class GovernorBountyConsoleBoundUserInterface : BoundUserInterface
{
    [ViewVariables]
    private GovernorBountyMenu? _menu;

    public GovernorBountyConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();

        if (_menu == null)
        {
            _menu = this.CreateWindow<GovernorBountyMenu>();
            _menu.OnAcceptButtonPressed += id => SendMessage(new GovernorBountyAcceptMessage(id));
            _menu.OnSkipButtonPressed += id => SendMessage(new GovernorBountySkipMessage(id));
            _menu.OnRedeemButtonPressed += () => SendMessage(new GovernorBountyRedeemMessage());
        }
    }

    protected override void UpdateState(BoundUserInterfaceState message)
    {
        base.UpdateState(message);

        if (message is not GovernorBountyConsoleState state)
            return;

        _menu?.UpdateEntries(state.Bounties, state.UntilNextSkip);
    }
}


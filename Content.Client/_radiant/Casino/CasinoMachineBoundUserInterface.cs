using Content.Shared._radiant.Casino;
using Robust.Client.UserInterface;

namespace Content.Client._radiant.Casino;

public sealed partial class CasinoMachineBoundUserInterface : BoundUserInterface
{
    private CasinoMachineWindow? _window;

    public CasinoMachineBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
    }

    protected override void Open()
    {
        base.Open();
        _window = this.CreateWindow<CasinoMachineWindow>();
        _window.Fragment.OnSpin += bet => SendMessage(new CasinoMachineSpinMessage(bet));
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is CasinoUiState casinoState)
            _window?.Fragment.UpdateState(casinoState);
    }
}

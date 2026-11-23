using Content.Client.UserInterface.Fragments;
using Content.Shared._radint.Cargo.BUI;
using Robust.Client.UserInterface;

namespace Content.Client._radint.CartridgeLoader.Cartridges;

public sealed partial class DynamicCargoMarketUi : UIFragment
{
    private DynamicCargoMarketUiFragment? _fragment;

    public override Control GetUIFragmentRoot()
    {
        return _fragment!;
    }

    public override void Setup(BoundUserInterface userInterface, EntityUid? fragmentOwner)
    {
        _fragment = new DynamicCargoMarketUiFragment();
    }

    public override void UpdateState(BoundUserInterfaceState state)
    {
        if (state is CargoMarketRatesUiState ratesState)
            _fragment?.UpdateState(ratesState);
    }
}
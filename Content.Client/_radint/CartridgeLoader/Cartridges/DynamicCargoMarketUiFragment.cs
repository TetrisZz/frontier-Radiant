using Content.Shared._radint.Cargo.BUI;
using Robust.Client.UserInterface.Controls;

namespace Content.Client._radint.CartridgeLoader.Cartridges;

public sealed class DynamicCargoMarketUiFragment : BoxContainer
{
    private readonly BoxContainer _rates;

    public DynamicCargoMarketUiFragment()
    {
        Orientation = LayoutOrientation.Vertical;
        HorizontalExpand = true;
        VerticalExpand = true;

        var scroll = new ScrollContainer
        {
            HScrollEnabled = false,
            VerticalExpand = true,
            HorizontalExpand = true,
        };
        _rates = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
        };
        scroll.AddChild(_rates);
        AddChild(scroll);
    }

    public void UpdateState(CargoMarketRatesUiState state)
    {
        _rates.RemoveAllChildren();

        foreach (var rate in state.Rates)
        {
            var row = new BoxContainer
            {
                Orientation = LayoutOrientation.Horizontal,
                HorizontalExpand = true,
                Margin = new Thickness(4),
            };
            row.AddChild(new Label
            {
                Text = rate.MarketName,
                HorizontalExpand = true,
                ClipText = true,
            });
            row.AddChild(new Label
            {
                Text = $"{rate.Multiplier:0.00}x",
                HorizontalAlignment = HAlignment.Right,
            });
            _rates.AddChild(row);
        }
    }
}
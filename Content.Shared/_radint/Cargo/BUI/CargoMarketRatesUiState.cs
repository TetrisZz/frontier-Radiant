using Robust.Shared.Serialization;

namespace Content.Shared._radint.Cargo.BUI;

[Serializable, NetSerializable]
public sealed class CargoMarketRatesUiState(List<CargoMarketRate> rates) : BoundUserInterfaceState
{
    public readonly List<CargoMarketRate> Rates = rates;
}

[Serializable, NetSerializable]
public sealed class CargoMarketRate(string marketName, float multiplier)
{
    public readonly string MarketName = marketName;
    public readonly float Multiplier = multiplier;
}
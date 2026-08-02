using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Casino;

[Serializable, NetSerializable]
public enum CasinoMachineUiKey : byte
{
    Key,
}

[Serializable, NetSerializable]
public sealed class CasinoMachineSpinMessage : BoundUserInterfaceMessage
{
    public readonly int Bet;

    public CasinoMachineSpinMessage(int bet)
    {
        Bet = bet;
    }
}


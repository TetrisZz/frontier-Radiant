namespace Content.Server._radiant.Casino;

[RegisterComponent]
public sealed partial class CasinoMachineComponent : Component
{
    [DataField]
    public int Bet = 1000;

    [DataField]
    public TimeSpan SpinCooldown = TimeSpan.FromSeconds(2);

    public TimeSpan NextSpinTime;
}

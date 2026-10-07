namespace Content.Server._radiant.Medical.Virology;

/// <summary>
/// An expedition zombie that may become a persistent carrier when a group encounters it.
/// </summary>
[RegisterComponent]
public sealed partial class VirologyZombieSourceComponent : Component
{
    public int CheckedCrowdTier;
    public string Disease = "";
    public float Accumulator;
    public float CoughClock;
}

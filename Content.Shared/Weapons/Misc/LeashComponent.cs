namespace Content.Shared.Weapons.Misc;

/// <summary>
/// Item that can leash another humanoid when held as the active hand item.
/// Values are copied onto <see cref="PlayerLeashPullerComponent"/> when a leash is started.
/// </summary>
[RegisterComponent]
public sealed partial class LeashComponent : Component
{
    [DataField]
    public float MaxForce = PlayerLeashPullerComponent.DefaultMaxForce;

    [DataField]
    public float Frequency = PlayerLeashPullerComponent.DefaultFrequency;

    [DataField]
    public float DampingRatio = PlayerLeashPullerComponent.DefaultDampingRatio;

    [DataField]
    public float MassLimit = PlayerLeashPullerComponent.DefaultMassLimit;

    [DataField]
    public float MaxLeashDistance = PlayerLeashPullerComponent.DefaultMaxLeashDistance;

    [DataField]
    public Color LineColor = Color.LightSkyBlue;

    [DataField] //Radiant Sector
    public int MaxLeashTargets = PlayerLeashPullerComponent.DefaultMaxLeashTargets;

    /// <summary>
    /// Time required to attach this leash to another humanoid (verb / interaction).
    /// </summary>
    [DataField]
    public float AttachDelaySeconds = 3f;
}

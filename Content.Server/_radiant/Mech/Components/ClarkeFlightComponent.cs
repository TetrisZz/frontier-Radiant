namespace Content.Server._radiant.Mech.Components;

/// <summary>
/// Provides the Clarke's built-in zero-gravity flight systems.
/// </summary>
[RegisterComponent]
public sealed partial class ClarkeFlightComponent : Component
{
    /// <summary>
    /// Battery charge consumed every second while the Clarke is flying in zero gravity.
    /// </summary>
    [DataField]
    public float EnergyUsePerSecond = 25f;

    /// <summary>
    /// Movement multiplier applied only while flying in zero gravity.
    /// </summary>
    [DataField]
    public float SpaceSpeedModifier = 0.1556f;

    /// <summary>
    /// Tracks the previous power state so movement modifiers are refreshed only when it changes.
    /// </summary>
    public bool WasPowered = true;

    /// <summary>
    /// Tracks whether the Clarke was flying so its movement speed is refreshed on gravity transitions.
    /// </summary>
    public bool WasFlying;
}

/// <summary>
/// Keeps the Clarke magnetically secured whenever it is on a grid or planet surface.
/// </summary>
[RegisterComponent]
public sealed partial class ClarkeMagbootsComponent : Component;

using Robust.Shared.GameStates;

namespace Content.Shared._radiant.Abilities.Feroxi;

/// <summary>
/// Grants feroxi an affinity for standing in water puddles.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FeroxiWaterAffinityComponent : Component
{
    [DataField]
    public float WaterSpeedModifier = 1.15f;

    [ViewVariables]
    public bool InWater;
}

/// <summary>
/// Marks a full water tile that grants the feroxi water bonus.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FeroxiWaterSourceComponent : Component;

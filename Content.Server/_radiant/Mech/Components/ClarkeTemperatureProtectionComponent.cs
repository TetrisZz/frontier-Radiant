using Content.Server.Temperature.Components;

namespace Content.Server._radiant.Mech.Components;

/// <summary>
/// Marks a Clarke cockpit that insulates its occupant from environmental heat and cold.
/// </summary>
[RegisterComponent]
public sealed partial class ClarkeTemperatureProtectionComponent : Component
{
    [DataField]
    public float HeatDamageThreshold = 10000f;

    [DataField]
    public float ColdDamageThreshold = 0f;
}

/// <summary>
/// Restores a pilot's own temperature thresholds when they leave the Clarke.
/// </summary>
[RegisterComponent]
public sealed partial class ClarkePilotTemperatureProtectionComponent : Component
{
    public float HeatDamageThreshold;
    public float ColdDamageThreshold;
    public float? ParentHeatDamageThreshold;
    public float? ParentColdDamageThreshold;
    public bool AddedTemperatureProtection;
}

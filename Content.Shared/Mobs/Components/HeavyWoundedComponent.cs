using Robust.Shared.GameStates;

namespace Content.Shared.Mobs.Components;

/// <summary>
/// Keeps a humanoid conscious but forced to the ground between the configured damage thresholds.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class HeavyWoundedComponent : Component
{
    [DataField]
    public float DamageThreshold = 100f;

    [DataField]
    public float CriticalThreshold = 115f;

    [AutoNetworkedField]
    public bool Active;

    /// <summary>
    /// Prevents the crawling state from immediately reapplying when a patient recovers from critical condition.
    /// </summary>
    [AutoNetworkedField]
    public bool RecoveredFromCritical;
}

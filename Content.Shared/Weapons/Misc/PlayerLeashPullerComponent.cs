using Robust.Shared.GameStates;

namespace Content.Shared.Weapons.Misc;

/// <summary>
/// The player dragging another mob with the player leash. Added only while an active tether exists.
/// </summary>
[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class PlayerLeashPullerComponent : Component
{
    public const float DefaultMaxForce = 380f;
    public const float DefaultFrequency = 12f;
    public const float DefaultDampingRatio = 2.5f;
    public const float DefaultMassLimit = 200f;
    public const float DefaultMaxLeashDistance = 14f;

    /// <summary> The tether anchor entity (<see cref="TetherEntity"/> prototype) used for physics. </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? TetherAnchor;

    /// <summary> The mob currently being dragged. </summary>
    [DataField, AutoNetworkedField]
    public EntityUid? Following;

    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public float MaxForce = DefaultMaxForce;

    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public float Frequency = DefaultFrequency;

    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public float DampingRatio = DefaultDampingRatio;

    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public float MassLimit = DefaultMassLimit;

    /// <summary> If the tether stretches beyond this, it breaks cleanly. </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public float MaxLeashDistance = DefaultMaxLeashDistance;

    /// <summary> Current leash length limit that can be adjusted while active. </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public float CurrentLeashDistance = 3f;

    /// <summary> Minimum leash length when reeling in. </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public float MinLeashDistance = 1f;

    /// <summary> Step used by leash length adjustment verbs. </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public float DistanceAdjustStep = 0.5f;

    [ViewVariables(VVAccess.ReadWrite), DataField, AutoNetworkedField]
    public Color LineColor = Color.LightSkyBlue;

    public const string LeashJointId = "player-leash";
}

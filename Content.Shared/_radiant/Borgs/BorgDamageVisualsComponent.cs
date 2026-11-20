using Robust.Shared.GameStates;

namespace Content.Shared._radiant.Borgs;

/// <summary>
/// Opts a chassis into its own fallen, critical and dead sprites. Derelict chassis deliberately do not use this.
/// Selected borg types use their spriteBodyState as the prefix instead of BodyState.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class BorgDamageVisualsComponent : Component
{
    [DataField]
    public string BodyState = "robot";

    [DataField]
    public string TippedSuffix = "_tipped";

    [DataField]
    public string CriticalSuffix = "_rest";

    [DataField]
    public string DeadSuffix = "_wreck";

    // Client-local depth to restore after revival.
    public int? OriginalDrawDepth;
}

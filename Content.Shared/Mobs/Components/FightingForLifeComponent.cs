using Robust.Shared.GameStates;

namespace Content.Shared.Mobs.Components;

/// <summary>
/// Temporarily lets a critical mob act while remaining in the critical state.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class FightingForLifeComponent : Component;

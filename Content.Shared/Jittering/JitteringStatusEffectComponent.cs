using Robust.Shared.GameStates;

namespace Content.Shared.Jittering;

/// <summary>
/// Jitter from timed status effects (<see cref="SharedJitteringSystem.DoJitter"/>).
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(SharedJitteringSystem))]
public sealed partial class JitteringStatusEffectComponent : Component;

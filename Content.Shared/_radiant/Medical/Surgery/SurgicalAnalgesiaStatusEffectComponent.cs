using Robust.Shared.GameStates;

namespace Content.Shared._radiant.Medical.Surgery;

/// <summary>
/// Temporary surgical pain suppression, on a status effect entity rather than the patient.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class SurgicalAnalgesiaStatusEffectComponent : Component;

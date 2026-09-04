using Robust.Shared.GameStates;

namespace Content.Shared._Starlight.Cybernetics.Components;

/// <summary>
/// Prevents the owner from distinguishing flavours.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class UnableToTasteComponent : Component;

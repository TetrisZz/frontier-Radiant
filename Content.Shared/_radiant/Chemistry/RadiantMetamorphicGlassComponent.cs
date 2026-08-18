using Robust.Shared.GameStates;

namespace Content.Shared._radiant.Chemistry;

/// <summary>
/// Marks custom Radiant drink glasses that must restore the regular glass sprite when emptied.
/// </summary>
[RegisterComponent, NetworkedComponent]
public sealed partial class RadiantMetamorphicGlassComponent : Component;

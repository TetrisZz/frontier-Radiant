namespace Content.Shared._Starlight.Cybernetics.Components;

/// <summary>
/// Multiplies the owner's normal thirst decay rate.
/// </summary>
[RegisterComponent]
public sealed partial class ThirstRateMultiplierComponent : Component
{
    [DataField]
    public float Multiplier = 1f;
}

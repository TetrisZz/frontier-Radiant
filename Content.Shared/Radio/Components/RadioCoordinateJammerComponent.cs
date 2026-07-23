namespace Content.Shared.Radio.Components;

/// <summary>
/// Hides the origin coordinates of radio messages sent from within its radius.
/// </summary>
[RegisterComponent]
public sealed partial class RadioCoordinateJammerComponent : Component
{
    /// <summary>
    /// Radius, in metres, in which radio coordinates are replaced with interference.
    /// </summary>
    [DataField("radius")]
    public float Radius = 300f;
}

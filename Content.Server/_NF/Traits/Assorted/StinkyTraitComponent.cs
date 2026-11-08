using System.Numerics;

namespace Content.Server._NF.Traits.Assorted;

/// <summary>
/// This is used for the stinky trait.
/// </summary>
[RegisterComponent, Access(typeof(StinkyTraitSystem))]
public sealed partial class StinkyTraitComponent : Component
{
    internal static readonly Vector2 DefaultIncidentInterval = new(300, 600);

    /// <summary>
    /// The random time between incidents, (min, max).
    /// </summary>
    [DataField("timeBetweenIncidents")]
    public Vector2 TimeBetweenIncidents { get; private set; } = DefaultIncidentInterval;

    public float NextIncidentTime;

    public bool IsActive = true;
}

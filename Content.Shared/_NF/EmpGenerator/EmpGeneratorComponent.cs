using Robust.Shared.Audio;
using Robust.Shared.GameStates;

namespace Content.Shared._NF.EmpGenerator;

[RegisterComponent, NetworkedComponent]
public sealed partial class EmpGeneratorComponent : Component
{
    // Radiant Sector: the EMP generator gives an audible warning before releasing its pulse.
    [DataField]
    public TimeSpan ActivationDelay = TimeSpan.FromSeconds(3.5);

    // Radiant Sector: match the EMP grenade's priming sound when the generator is activated.
    [DataField]
    public SoundSpecifier? ActivationSound = new SoundPathSpecifier("/Audio/Effects/countdown.ogg");

    // Radiant Sector: server-side timer for the pending EMP discharge.
    [ViewVariables]
    public TimeSpan? PendingPulseAt;

    /// <summary>
    /// The range of the EMP blast to spawn.
    /// </summary>
    [DataField]
    public float Range = 100.0f;

    /// <summary>
    /// How much energy will be consumed per battery in range
    /// </summary>
    [DataField]
    public float EnergyConsumption = 1000000;

    /// <summary>
    /// How long it disables targets in seconds
    /// </summary>
    [DataField]
    public float DisableDuration = 60f;

    [DataField(serverOnly: true)]
    public float LightRadiusMin { get; set; }

    [DataField(serverOnly: true)]
    public float LightRadiusMax { get; set; }
}

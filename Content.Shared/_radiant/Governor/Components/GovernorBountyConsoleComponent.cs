using Content.Shared._radiant.Governor;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._radiant.Governor.Components;

[RegisterComponent]
public sealed partial class GovernorBountyConsoleComponent : Component
{
    [DataField(required: true)]
    public string ItemContainer;

    /// <summary>
    /// The paper entity spawned when a bounty is accepted.
    /// </summary>
    [DataField]
    public EntProtoId BountyLabelId = "PaperGovernorBountyManifest";

    [DataField]
    public List<GovernorBountyData> Bounties = new();

    [DataField]
    public int MaxBounties = 5;                 // сколько заданий максимум

    [DataField]
    public int TotalBounties;                   // счётчик: сколько всего заданий создано (для номеров)

    [DataField]
    public TimeSpan NextSkipTime = TimeSpan.Zero;   // когда можно снова пропускать

    [DataField]
    public TimeSpan SkipDelay = TimeSpan.FromMinutes(15);   // задержка между скипами

    [DataField]
    public SoundSpecifier AcceptSound = new SoundPathSpecifier("/Audio/Effects/Cargo/ping.ogg");

    [DataField]
    public SoundSpecifier DenySound = new SoundPathSpecifier("/Audio/Effects/Cargo/buzz_two.ogg");

    /// <summary>
    /// The sound made when printing the manifest.
    /// </summary>
    [DataField]
    public SoundSpecifier PrintSound = new SoundPathSpecifier("/Audio/Machines/printer.ogg");

    /// <summary>
    /// The time at which the console will accept the next bounty.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    public TimeSpan NextPrintTime = TimeSpan.Zero;

    /// <summary>
    /// The delay between accepting bounties.
    /// </summary>
    [DataField]
    public TimeSpan PrintDelay = TimeSpan.FromSeconds(1.5);
}




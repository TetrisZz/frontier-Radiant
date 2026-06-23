using System.Numerics;
using Content.Shared.Salvage;
using Content.Shared.Salvage.Expeditions;
using Robust.Shared.Audio;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom.Prototype.List;

namespace Content.Server.Salvage.Expeditions;

/// <summary>
/// Designates this entity as holding a salvage expedition.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class SalvageExpeditionComponent : SharedSalvageExpeditionComponent
{
    public SalvageMissionParams MissionParams = default!;

    /// <summary>
    /// Where the dungeon is located for initial announcement.
    /// </summary>
    [DataField("dungeonLocation")]
    public Vector2 DungeonLocation = Vector2.Zero;

    /// <summary>
    /// When the expeditions ends.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField("endTime", customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan EndTime;

    /// <summary>
    ///     Has the emergency rescue sweep already run for this expedition.///Radiant Sector
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public bool RescueStarted;

    /// <summary>
    ///     How long before expedition end to extract critical or dead players.///Radiant Sector
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public TimeSpan RescueTimeBeforeEnd = TimeSpan.FromMinutes(1);

    /// <summary>
    ///     Delay before the implanted emergency medicine activates.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public TimeSpan RescueImplantDelay = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     Emergency pod to use when no empty pod already exists on the shuttle.///Radiant Sector
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public EntProtoId RescuePodPrototype = "MedicalPod";

    /// <summary>
    ///     Implant injected into rescued players.///Radiant Sector
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public EntProtoId RescueImplantPrototype = "ExpeditionRescueMedicalImplant";

    /// <summary>
    /// Station whose mission this is.
    /// </summary>
    [DataField("station")]
    public EntityUid Station;

    [ViewVariables] public bool Completed = false;

    // Frontier: moved to Client
    /// <summary>
    /// Countdown audio stream.
    /// </summary>
    // [DataField, AutoNetworkedField]
    // public EntityUid? Stream = null;
    // End Frontier: moved to Client

    /// <summary>
    /// Sound that plays when the mission end is imminent.
    /// </summary>
    [ViewVariables(VVAccess.ReadWrite), DataField]
    public SoundSpecifier Sound = new SoundCollectionSpecifier("ExpeditionEnd")
    {
        Params = AudioParams.Default.WithVolume(-5),
    };

    // Frontier: moved to Shared
    /// <summary>
    /// Song selected on MapInit so we can predict the audio countdown properly.
    /// </summary>
    // [DataField]
    // public ResolvedSoundSpecifier SelectedSong;
    // End Frontier: moved to Shared
}

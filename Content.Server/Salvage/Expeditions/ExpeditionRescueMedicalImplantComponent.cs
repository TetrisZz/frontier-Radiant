using Content.Server.Salvage;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Server.Salvage.Expeditions;

/// <summary>
///     Emergency medical implant used by salvage expedition auto-rescue.
/// </summary>
[RegisterComponent, AutoGenerateComponentPause, Access(typeof(SalvageSystem))]
public sealed partial class ExpeditionRescueMedicalImplantComponent : Component
{
    /// <summary>
    ///     When the implant should activate.
    /// </summary>
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer))]
    [AutoPausedField]
    public TimeSpan ActivateAt;

    /// <summary>
    ///     Delay after death before the medical alert is sent.
    /// </summary>
    [DataField]
    public TimeSpan AlertDelay = TimeSpan.FromMinutes(2);

    /// <summary>
    ///     Prevents repeated alerts once the implant has fired.
    /// </summary>
    [DataField]
    public bool Activated;

    /// <summary>
    ///     Name to report if the entity name changes after rescue.
    /// </summary>
    [DataField]
    public string PatientName = string.Empty;

    /// <summary>
    ///     Shuttle name reported to medical.
    /// </summary>
    [DataField]
    public string ShuttleName = string.Empty;
}

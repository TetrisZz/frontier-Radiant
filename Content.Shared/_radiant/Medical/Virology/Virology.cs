using Content.Shared.DoAfter;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Medical.Virology;

[Prototype("radiantDisease")]
public sealed partial class RadiantDiseasePrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public string Name = "";
    [DataField(required: true)] public string Description = "";
    [DataField(required: true)] public string Reagent = "";
    [DataField(required: true)] public string Route = "";
    [DataField] public float Incubation = 180;
    [DataField] public float SevereAfter = 660;
    [DataField] public float RecoveryAfter = 1500;
    [DataField] public float ImmunitySeconds = 1200;
    [DataField] public float TreatmentSeconds = 120;
    [DataField] public float ExposureThreshold = 100;
    [DataField] public float ExposureDecay = 0.25f;
    [DataField] public float CoughDose = 35;
    [DataField] public float LiquidDose = 2000;
    [DataField] public float MildSpeed = 0.95f;
    [DataField] public float SevereSpeed = 0.9f;
    [DataField] public float BloodRegenerationMultiplier = 1;
    [DataField] public float BloodLossPerPulse;
    [DataField] public float SevereBloodLossPerPulse;
}

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState]
public sealed partial class VirologySymptomsComponent : Component
{
    [AutoNetworkedField] public int Severity;
    [AutoNetworkedField] public float SpeedMultiplier = 1;
    [AutoNetworkedField] public float BloodRegenerationMultiplier = 1;
}

[RegisterComponent]
public sealed partial class VirologyProtectionComponent : Component
{
    [DataField] public float DropletMultiplier = 0.3f;
}

[Serializable, NetSerializable]
public enum VirologyUiKey : byte { Key }
[Serializable, NetSerializable]
public enum VirologyMachineVisuals : byte { Running, Powered }
[Serializable, NetSerializable]
public enum VirologyCommand : byte { Analyze, Treat, Vaccinate, Eject, Select }
[Serializable, NetSerializable]
public sealed class VirologyMessage(VirologyCommand command, string disease = "") : BoundUserInterfaceMessage
{
    public readonly VirologyCommand Command = command;
    public readonly string Disease = disease;
}
[Serializable, NetSerializable]
public sealed class VirologyState : BoundUserInterfaceState
{
    public string Report = "";
    public string Archive = "";
    public string Status = "";
    public string Selected = "";
    public Dictionary<string, string> Diseases = new();
    public bool CanAnalyze;
    public bool CanProduce;
    public bool CanEject;
    public bool Producer;
    public bool Working;
    public float Progress;
}
[Serializable, NetSerializable]
public sealed partial class VirologyUseDoAfterEvent : SimpleDoAfterEvent;

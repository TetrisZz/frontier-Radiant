using Content.Shared._radiant.Skills;
using Content.Shared.Roles;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Careers;

[Prototype]
public sealed partial class CareerProfessionPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public LocId Name;
    [DataField] public LocId? Description;
    [DataField] public EntProtoId? Icon;
    [DataField] public List<string> Jobs = new();
    [DataField] public ProtoId<JobPrototype>? InterviewJob;
    /// <summary>Only this profession may use this shipyard, and its members may only buy ships there.</summary>
    [DataField] public EntProtoId? ExclusiveShipyard;
    [DataField] public ProfessionalSkill? RequiredSkill;
    [DataField] public int RequiredSkillLevel;
    [DataField] public List<ProtoId<CareerLevelPrototype>> Levels = new();
    [DataField] public int CrewSize = 3;
    [DataField] public List<CareerJobTitle> JobTitles = new();
}

[DataDefinition]
public sealed partial class CareerJobTitle
{
    [DataField(required: true)] public string Id = default!;
    [DataField(required: true)] public LocId Name;
    [DataField] public int RequiredLevel;
}

[Serializable, NetSerializable]
public enum CareerObjectiveType : byte
{
    SaleValue,
    SaleQuantity,
    DutyTime,
    Damage,
    Healing,
    ChemicalHealing,
    Synthesis,
    Implantation,
    GeneticSequence,
    GeneticProduction,
    DiseaseAnalysis,
    DiseaseProduction,
    DiseaseTreatment,
    Construction,
}

[DataDefinition]
public sealed partial class CareerObjective
{
    [DataField(required: true)] public string Id = default!;
    [DataField(required: true)] public LocId Title;
    [DataField(required: true)] public CareerObjectiveType Type;
    [DataField(required: true)] public double Target;
    [DataField] public HashSet<string> Prototypes = new();
    [DataField] public HashSet<string> Tags = new();
    [DataField] public HashSet<string> Qualifiers = new();
    [DataField] public HashSet<string> SaleLocations = new() { "Trade" };
}

[Prototype]
public sealed partial class CareerLevelPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public List<CareerObjective> Objectives = new();
    [DataField] public List<EntProtoId> RewardItems = new();
    [DataField] public int RewardMoney;
    [DataField] public int BoxTier;
}

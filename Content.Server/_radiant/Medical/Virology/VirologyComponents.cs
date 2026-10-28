using Content.Shared._radiant.Medical.Virology;
using Robust.Shared.Containers;

namespace Content.Server._radiant.Medical.Virology;

[RegisterComponent]
public sealed partial class VirologyCarrierComponent : Component
{
    [DataField] public Dictionary<string, float> Infections = new();
    [DataField] public Dictionary<string, float> Exposure = new();
    [DataField] public Dictionary<string, float> Immunity = new();
    [DataField] public Dictionary<string, float> Treatment = new();
    public float Accumulator;
    public float SymptomClock;
}

[RegisterComponent]
public sealed partial class VirologySampleComponent : Component
{
    [DataField] public bool Collected;
    [DataField] public bool Analyzed;
    [DataField] public string Donor = "";
    [DataField] public List<string> Diseases = new();
    [DataField] public VirologyArchiveMatch ArchiveMatch;
    [DataField] public int ArchiveProfile;
    [DataField] public List<string> SharedDiseases = new();
    [DataField] public HashSet<string> KnownSymptoms = new();
}

public enum VirologyArchiveMatch : byte
{
    None,
    New,
    Exact,
    Related,
    Negative,
}

[DataDefinition]
public sealed partial class VirologyArchiveRecord
{
    [DataField] public int Number;
    [DataField] public List<string> Diseases = new();
    [DataField] public string FirstDonor = "";
    [DataField] public string LastDonor = "";
    [DataField] public int Samples;
}

[RegisterComponent]
public sealed partial class VirologyArchiveComponent : Component
{
    [DataField] public HashSet<string> KnownSymptoms = new();
    [DataField] public List<VirologyArchiveRecord> Records = new();
}

[RegisterComponent]
public sealed partial class VirologyOrganComponent : Component
{
    [DataField] public List<string> Diseases = new();
}

[RegisterComponent]
public sealed partial class VirologyDoseComponent : Component
{
    [DataField] public string Disease = "";
    [DataField] public bool Vaccine;
    [DataField] public bool Used;
}

[RegisterComponent]
public sealed partial class VirologyMachineComponent : Component
{
    [DataField] public int Tier = 1;
    [DataField] public float StudySeconds = 90;
    [DataField] public float CultureSeconds = 90;
    public List<string> Draft = new();
    public string PendingProfile = "";
    [DataField] public bool Producer;
    [DataField] public float AnalysisSeconds = 30;
    [DataField] public float ProductionSeconds = 60;
    [DataField] public int BiomassCost = 5;
    public ContainerSlot Sample = default!;
    public string Selected = "";
    public VirologyCommand? Work;
    public float Remaining;
    public float Duration;
    public float UiAccumulator;
    public Content.Server._radiant.Careers.MedicalCredit? CareerOperator;
}

using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Medical.Genetics;

[Serializable, NetSerializable]
public enum GeneticBodyUiKey : byte { Key }

[Serializable, NetSerializable]
public enum GeneticProduct : byte { Body, Heart, Lungs, Liver, Kidneys, Stomach, Eyes, Tongue, Hematopoiesis, HematopoiesisRemoval, Coagulation, CoagulationRemoval, Regeneration, RegenerationRemoval, Ultravision, UltravisionRemoval, Strength, StrengthRemoval, Sobriety, SobrietyRemoval, Insulation, InsulationRemoval }

[Serializable, NetSerializable]
public enum GeneticCommand : byte { Analyze, Grow, Release, Eject, Select, Research, SelectGene, CheckSequence, EjectDisk, Spectrum }

[Serializable, NetSerializable]
public sealed class GeneticBodyMessage(GeneticCommand command, GeneticProduct product = GeneticProduct.Body, string text = "") : BoundUserInterfaceMessage
{
    public readonly string Text = text;
    public readonly GeneticCommand Command = command;
    public readonly GeneticProduct Product = product;
}

[Serializable, NetSerializable]
public sealed class GeneticBodyState : BoundUserInterfaceState
{
    public string Sample = "";
    public NetEntity? SampleEntity;
    public string Status = "";
    public string Warning = "";
    public string Profile = "";
    public string NextStep = "";
    public string ProductDescription = "";
    public Dictionary<string, string> Genes = new();
    public string SelectedGene = "";
    public string GeneDescription = "";
    public string SequencePattern = "";
    public string SequenceFeedback = "";
    public bool CanCheckSequence;
    public bool HasDisk;
    public bool CanEjectDisk;
    public string Connection = "";
    public string ProductionStatus = "";
    public string Archive = "";
    public string Composition = "";
    public bool CanSpectrum;
    public bool CanResearch;
    public int Biomass;
    public int Cost;
    public int Seconds;
    public float Progress;
    public bool CanAnalyze;
    public bool CanGrow;
    public bool CanRelease;
    public bool CanEject;
    public bool CanSelect;
    public GeneticProduct Selected;
    public List<GeneticProduct> Products = new();
}

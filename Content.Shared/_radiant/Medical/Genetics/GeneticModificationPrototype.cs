using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Medical.Genetics;

/// <summary>Shared so clients can load genetic recipe prototypes as well as servers.</summary>
[Prototype]
public sealed partial class GeneticModificationPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public string Name = "";
    [DataField] public int Load = 40;
    [DataField] public float AdaptationSeconds = 120;
    [DataField] public float BloodPerSecond = 0.05f;
    [DataField] public float NutritionPerBlood = 0.5f;
    [DataField] public float ClottingPerSecond;
    [DataField] public float HealingPerSecond;
    [DataField] public float NutritionPerSecond;
    [DataField] public List<string> Conflicts = new();
    [DataField] public string ResearchHint = "genetics-hint-blood";
    [DataField] public string? ExamineHint;
    [DataField] public float ComplicationChance = 0.15f;
    [DataField] public List<string> Complications = new() { "weakness", "slurred", "paracusia" };
}

using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared._radiant.ResearchBlueprints;

[Prototype("technologyBlueprints")]
public sealed partial class TechnologyBlueprintsPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public LocId Name;
    [DataField(required: true)] public EntProtoId EntityIcon;
    [DataField(required: true)] public ProtoId<TechDisciplinePrototype> Discipline;
    [DataField] public double Rare = 100;
    [DataField] public LocId RareLoc = "research-technology-blueprint-common";
    [DataField] public int Tier = 1;
    [DataField] public List<int> Tiers = new();
    [DataField(required: true)] public EntProtoId Lathe;
    [DataField] public List<EntProtoId> AdditionalLathes = new();
    [DataField] public List<ProtoId<LatheRecipePrototype>> Recipe = new();
    [DataField] public List<ProtoId<LatheRecipePrototype>> RecipeForce = new();
    [DataField] public List<List<ProtoId<LatheRecipePrototype>>> RecipeLinks = new();
    public bool SupportsTier(int tier) => Tier == tier || Tiers.Contains(tier);
}

[Prototype("latheBlueprint")]
public sealed partial class LatheBlueprintPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public List<EntProtoId> Lathe = new();
}

[Prototype("blueprintMaterialTier")]
public sealed partial class BlueprintMaterialTierPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField(required: true)] public List<ProtoId<MaterialPrototype>> Materials = new();
    [DataField] public int Minimum;
    [DataField] public int Maximum;
    [DataField] public double Weight = 1;
}

[Prototype("blueprintResearch")]
public sealed partial class BlueprintResearchPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public int Cost = 30000;
    [DataField] public int Tier = 1;
    [DataField] public EntProtoId Blueprint = "ResearchRandomBlueprint";
    [DataField] public double TimeChance = 0.25;
    [DataField] public double MaterialChance = 0.25;
    [DataField] public double ExtraMaterialChance = 0.25;
    [DataField] public double LatheChance = 0.05;
    [DataField] public double UnmodifiedChance = 0.005;
    [DataField] public List<ProtoId<BlueprintMaterialTierPrototype>> MaterialTiers = new();
}

using Content.Shared.Lathe;
using Content.Shared.Materials;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;

namespace Content.Shared._radiant.ResearchBlueprints;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class BlueprintModifiers
{
    [DataField] public string InstanceId = string.Empty;
    [DataField] public ProtoId<TechnologyBlueprintsPrototype>? Technology;
    [DataField] public LocId RareLoc = "research-technology-blueprint-common";
    [DataField] public int TimeMultiplier = 1;
    [DataField] public int MaterialMultiplier = 1;
    [DataField] public Dictionary<ProtoId<MaterialPrototype>, int> ExtraMaterials = new();
    [DataField] public EntProtoId? Lathe;
    [DataField] public List<EntProtoId> AdditionalLathes = new();

    public IEnumerable<EntProtoId> GetLathes()
    {
        if (Lathe is { } primary) yield return primary;
        foreach (var machine in AdditionalLathes) yield return machine;
    }

    public string ShortId => InstanceId.Length > 8 ? InstanceId[..8].ToUpperInvariant() : InstanceId.ToUpperInvariant();

    public BlueprintModifiers Copy() => new()
    {
        InstanceId = InstanceId, Technology = Technology, RareLoc = RareLoc,
        TimeMultiplier = TimeMultiplier, MaterialMultiplier = MaterialMultiplier,
        ExtraMaterials = new(ExtraMaterials), Lathe = Lathe, AdditionalLathes = new(AdditionalLathes),
    };

    public static Dictionary<ProtoId<MaterialPrototype>, int> Materials(LatheRecipePrototype recipe,
        BlueprintModifiers? modifiers, float machineMultiplier)
    {
        var materials = new Dictionary<ProtoId<MaterialPrototype>, int>(recipe.Materials);
        var multiplier = modifiers?.MaterialMultiplier ?? 1;
        foreach (var material in new List<ProtoId<MaterialPrototype>>(materials.Keys))
            materials[material] = checked(materials[material] * multiplier);
        if (modifiers != null)
            foreach (var (material, amount) in modifiers.ExtraMaterials)
                materials[material] = checked(materials.GetValueOrDefault(material) + amount);
        foreach (var material in new List<ProtoId<MaterialPrototype>>(materials.Keys))
            materials[material] = SharedLatheSystem.AdjustMaterial(materials[material], recipe.ApplyMaterialDiscount, machineMultiplier);
        return materials;
    }

    public static TimeSpan Time(LatheRecipePrototype recipe, BlueprintModifiers? modifiers, float machineMultiplier)
        => recipe.CompleteTime * (modifiers?.TimeMultiplier ?? 1) * machineMultiplier;
}

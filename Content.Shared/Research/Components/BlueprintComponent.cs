using Content.Shared._NF.Lathe;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared.Research.Prototypes;
using Content.Shared.Research.Systems;
using Robust.Shared.GameStates;
using Robust.Shared.Prototypes;
using Robust.Shared.Utility;

namespace Content.Shared.Research.Components;

/// <summary>
/// This is used for an item that is inserted directly into a given lathe to provide it with a recipe.
/// </summary>
[RegisterComponent, NetworkedComponent, Access(typeof(BlueprintSystem), typeof(SharedBlueprintLatheSystem), typeof(SharedBlueprintResearchSystem), typeof(Content.Shared.Lathe.SharedLatheSystem))]
[AutoGenerateComponentState] // Frontier: dynamically set blueprints
public sealed partial class BlueprintComponent : Component
{
    /// <summary>
    /// The recipes that this blueprint provides.
    /// </summary>
    [DataField(required: true)]
    [AutoNetworkedField] // Frontier: dynamically set blueprints
    public HashSet<ProtoId<LatheRecipePrototype>> ProvidedRecipes = new();

    [DataField, AutoNetworkedField]
    public BlueprintModifiers? Modifiers;

    /// <summary>Roll fabrication modifiers once when a populated blueprint spawns.</summary>
    [DataField]
    public bool RandomizeModifiers = true;

    [DataField]
    public ProtoId<BlueprintResearchPrototype> ModifierPreset = "Default";

    /// <summary>Unspecified prototype blueprints are unique, not common research results.</summary>
    [DataField]
    public LocId Rarity = "research-technology-blueprint-unique";

    [DataField]
    public EntProtoId? ModifierLathe;

    [DataField]
    public List<EntProtoId> ModifierAdditionalLathes = new();
}

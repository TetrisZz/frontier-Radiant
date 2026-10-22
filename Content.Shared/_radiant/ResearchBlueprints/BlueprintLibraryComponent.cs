using Content.Shared.Research.Prototypes;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Utility;
using System;

namespace Content.Shared._radiant.ResearchBlueprints;

[RegisterComponent]
public sealed partial class BlueprintLibraryComponent : Component
{
    public EntityUid? LastServer;
}

[Serializable, NetSerializable]
public enum BlueprintLibraryUiKey : byte { Key }

[Serializable, NetSerializable]
public sealed class BlueprintLibrarySelectMessage(string recipe, NetEntity blueprint) : BoundUserInterfaceMessage
{
    public string Recipe = recipe;
    public NetEntity Blueprint = blueprint;
}

[Serializable, NetSerializable]
public sealed class BlueprintLibraryUnselectMessage(
    string recipe,
    NetEntity blueprint) : BoundUserInterfaceMessage
{
    public string Recipe = recipe;
    public NetEntity Blueprint = blueprint;
}

[Serializable, NetSerializable]
public sealed class BlueprintLibraryEjectMessage(NetEntity blueprint) : BoundUserInterfaceMessage
{
    public NetEntity Blueprint = blueprint;
}

[Serializable, NetSerializable]
public sealed class BlueprintLibraryEntry(NetEntity entity, List<ProtoId<LatheRecipePrototype>> recipes, BlueprintModifiers modifiers)
{
    public NetEntity Entity = entity;
    public List<ProtoId<LatheRecipePrototype>> Recipes = recipes;
    public BlueprintModifiers Modifiers = modifiers;
    public Dictionary<ProtoId<LatheRecipePrototype>, List<BlueprintLibraryDestination>> Destinations = new();
}

[Serializable, NetSerializable]
public sealed class BlueprintLibraryDestination(NetEntity entity, string name, EntProtoId prototype)
{
    public NetEntity Entity = entity;
    public string Name = name;
    public EntProtoId Prototype = prototype;
}

[Serializable, NetSerializable]
public sealed class BlueprintLibraryState(List<BlueprintLibraryEntry> blueprints,
    Dictionary<ProtoId<LatheRecipePrototype>, string> selected, bool connected) : BoundUserInterfaceState
{
    public List<BlueprintLibraryEntry> Blueprints = blueprints;
    public Dictionary<ProtoId<LatheRecipePrototype>, string> Selected = selected;
    public bool Connected = connected;
    public string ServerName = string.Empty;
    public int ServerId = -1;
}

[Serializable, NetSerializable]
public sealed class ResearchBlueprintRollMessage : BoundUserInterfaceMessage;

[DataDefinition, Serializable, NetSerializable]
public sealed partial class BlueprintResearchResult
{
    [DataField] public string InstanceId = string.Empty;
    [DataField] public ProtoId<TechnologyBlueprintsPrototype> Technology;
    [DataField] public List<ProtoId<LatheRecipePrototype>> Recipes = new();
    [DataField] public LocId RareLoc = "research-technology-blueprint-common";
}

[Serializable, NetSerializable]
public sealed class ResearchBlueprintConsoleState(int points, int cost, bool connected,
    List<BlueprintResearchResult> history) : BoundUserInterfaceState
{
    public int Points = points;
    public int Cost = cost;
    public bool Connected = connected;
    public List<BlueprintResearchResult> History = history;
    public bool Printing { get; set; }
    public float PrintRemaining { get; set; }
    public float PrintProgress { get; set; }
}

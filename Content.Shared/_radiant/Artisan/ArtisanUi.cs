using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Artisan;

[Serializable, NetSerializable]
public enum ArtisanUiKey : byte { Key }
[Serializable, NetSerializable]
public enum ArtisanVisuals : byte { Active, Loaded, Layer }
[Serializable, NetSerializable]
public enum ArtisanAction : byte { Recipe, Process, Reheat, Eject, Stop, Bottle, Settings, Source, ToggleTap }

[Serializable, NetSerializable]
public sealed class ArtisanActionMessage(ArtisanAction action, string recipe = "", NetEntity? item = null,
    string batchName = "", int amount = 30, bool applySettings = false) : BoundUserInterfaceMessage
{
    public ArtisanAction Action = action;
    public string Recipe = recipe;
    public NetEntity? Item = item;
    public string BatchName = batchName;
    public int Amount = amount;
    public bool ApplySettings = applySettings;
}

[Serializable, NetSerializable]
public sealed class ArtisanRecipeEntry(string id, string name, bool enabled, string reason, string result = "", List<ArtisanRequirementEntry>? requirements = null)
{
    public string Id = id;
    public string Name = name;
    public bool Enabled = enabled;
    public string Reason = reason;
    public string Result = result;
    public List<ArtisanRequirementEntry> Requirements = requirements ?? new();
}

[Serializable, NetSerializable]
public sealed class ArtisanRequirementEntry(string text, string? icon, bool? satisfied)
{
    public string Text = text;
    public string? Icon = icon;
    public bool? Satisfied = satisfied;
}

[Serializable, NetSerializable]
public sealed class ArtisanItemEntry(NetEntity entity, string name, string step, bool process, bool reheat, bool hot,
    string prototype = "", string result = "", List<ArtisanRequirementEntry>? requirements = null)
{
    public NetEntity Entity = entity;
    public string Name = name;
    public string Step = step;
    public bool Process = process;
    public bool Reheat = reheat;
    public bool Hot = hot;
    public string Prototype = prototype;
    public string Result = result;
    public List<ArtisanRequirementEntry> Requirements = requirements ?? new();
}

[Serializable, NetSerializable]
public sealed class ArtisanUiState(string title, ArtisanStationKind kind, string status, float progress,
    float temperature, string liquids, List<ArtisanRecipeEntry> recipes, List<ArtisanItemEntry> items,
    string batchName, int bottleAmount, bool busy, bool drainMode = false) : BoundUserInterfaceState
{
    public string Title = title;
    public ArtisanStationKind Kind = kind;
    public string Status = status;
    public float Progress = progress;
    public float Temperature = temperature;
    public string Liquids = liquids;
    public List<ArtisanRecipeEntry> Recipes = recipes;
    public List<ArtisanItemEntry> Items = items;
    public string BatchName = batchName;
    public int BottleAmount = bottleAmount;
    public bool Busy = busy;
    public bool DrainMode = drainMode;
}

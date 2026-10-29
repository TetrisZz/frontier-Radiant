using System.Threading.Tasks;
using Content.Shared.Chemistry.Components;
using Content.Shared.Research.Components;
using Robust.Shared.Serialization;

namespace Content.Server._radiant.Machines;

[RegisterComponent]
public sealed partial class PersistentMachineComponent : Component
{
    [DataField(required: true)] public string StorageId = string.Empty;
    public bool Ready;
    public bool Failed;
    public bool Restoring;
    public bool Closing;
    public string Kind = string.Empty;
    public string? LastPayload;
    public Task Loading = Task.CompletedTask;
    public bool Available => Ready && !Failed && !Closing;
}

[DataDefinition]
public sealed partial class MachineStorageSnapshot
{
    [DataField] public int Version = 1;
    [DataField] public List<MachineStoredBlueprint> Blueprints = new();
    [DataField] public Solution Chemicals = new();
}

[DataDefinition]
public sealed partial class MachineStoredBlueprint
{
    [DataField(required: true)] public string Prototype = string.Empty;
    [DataField] public string Name = string.Empty;
    [DataField(required: true)] public BlueprintComponent Blueprint = new();
}

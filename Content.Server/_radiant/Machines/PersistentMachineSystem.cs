using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Content.Server.Chemistry.Components;
using Content.Server.Database;
using Content.Server._radiant.ResearchBlueprints;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Research.Components;
using Content.Shared.UserInterface;
using Robust.Shared.Asynchronous;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization.Manager;
using Robust.Shared.Serialization.Markdown;
using Robust.Shared.Timing;
using YamlDotNet.RepresentationModel;

namespace Content.Server._radiant.Machines;

/// <summary>One live machine per configured key. DB I/O never touches entities off the game thread.</summary>
public sealed class PersistentMachineSystem : EntitySystem
{
    [Dependency] private readonly IServerDbManager _db = default!;
    [Dependency] private readonly ISerializationManager _serialization = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly ITaskManager _tasks = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;

    private readonly Dictionary<string, EntityUid> _claims = new();
    private readonly Dictionary<string, Task> _writes = new();
    private readonly Dictionary<EntityUid, TimeSpan> _pending = new();

    public override void Initialize()
    {
        SubscribeLocalEvent<PersistentMachineComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<PersistentMachineComponent, ComponentShutdown>(OnShutdown);
        SubscribeLocalEvent<PersistentMachineComponent, ActivatableUIOpenAttemptEvent>((uid, comp, args) =>
        { if (!comp.Available) args.Cancel(); });
        SubscribeLocalEvent<PersistentMachineComponent, ContainerIsInsertingAttemptEvent>((uid, comp, args) =>
        { if (IsInventoryContainer(uid, args.Container.ID) && !comp.Available && !comp.Restoring && !comp.Closing) args.Cancel(); });
        SubscribeLocalEvent<PersistentMachineComponent, ContainerIsRemovingAttemptEvent>((uid, comp, args) =>
        { if (IsInventoryContainer(uid, args.Container.ID) && !comp.Available && !comp.Restoring && !comp.Closing && !TerminatingOrDeleted(uid)) args.Cancel(); });
        SubscribeLocalEvent<PersistentMachineComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<PersistentMachineComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<PersistentMachineComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<PersistentMachineComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<PersistentMachineComponent, ExaminedEvent>((uid, comp, args) =>
            args.PushMarkup(Loc.GetString(comp.Failed ? "persistent-machine-error" : comp.Ready ? "persistent-machine-ready" : "persistent-machine-loading")));
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);
    }

    private void OnInit(Entity<PersistentMachineComponent> ent, ref MapInitEvent args)
    {
        if (string.IsNullOrWhiteSpace(ent.Comp.StorageId) || ent.Comp.StorageId.Length > 128 ||
            _claims.TryGetValue(ent.Comp.StorageId, out var owner) && owner != ent.Owner && Exists(owner))
        {
            ent.Comp.Failed = true;
            Log.Warning($"Persistent machine {ToPrettyString(ent)} has an invalid or duplicate storage key '{ent.Comp.StorageId}'.");
            return;
        }
        ent.Comp.Kind = HasComp<BlueprintLibraryComponent>(ent) ? "blueprints" : HasComp<ChemMasterComponent>(ent) ? "chemicals" : "";
        if (ent.Comp.Kind == "") { ent.Comp.Failed = true; return; }
        _claims[ent.Comp.StorageId] = ent;
        _writes.TryGetValue(ent.Comp.StorageId, out var previous);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ent.Comp.Loading = finished.Task;
        _ = Load(ent.Owner, ent.Comp.StorageId, previous, finished);
    }

    private async Task Load(EntityUid uid, string key, Task? previous, TaskCompletionSource finished)
    {
        try
        {
            if (previous != null) await previous.ConfigureAwait(false);
            var row = await _db.GetMachineStorageAsync(key).ConfigureAwait(false);
            _tasks.RunOnMainThread(() =>
            {
                try
                {
                    if (!TryComp<PersistentMachineComponent>(uid, out var comp) || comp.Closing || TerminatingOrDeleted(uid)) return;
                    if (row != null)
                    {
                        if (row.Kind != comp.Kind) throw new InvalidDataException("The stored machine kind does not match this prototype.");
                        var yaml = new YamlStream();
                        using var reader = new StringReader(row.Payload);
                        yaml.Load(reader);
                        var snapshot = _serialization.Read<MachineStorageSnapshot>(yaml.Documents.Single().RootNode.ToDataNode(), notNullableOverride: true);
                        Restore((uid, comp), snapshot);
                        comp.LastPayload = row.Payload;
                    }
                    comp.Ready = true;
                    comp.Failed = false;
                    if (HasComp<BlueprintLibraryComponent>(uid))
                        EntityManager.System<BlueprintResearchSystem>().UpdateLibrary(uid);
                    _ = SaveNow(uid);
                }
                catch (Exception error)
                {
                    if (TryComp<PersistentMachineComponent>(uid, out var comp)) comp.Failed = true;
                    Log.Error($"Failed restoring persistent machine '{key}': {error}");
                }
                finally { finished.TrySetResult(); }
            });
        }
        catch (Exception error)
        {
            _tasks.RunOnMainThread(() =>
            {
                if (TryComp<PersistentMachineComponent>(uid, out var comp)) comp.Failed = true;
                Log.Error($"Failed reading persistent machine '{key}': {error}");
                finished.TrySetResult();
            });
        }
    }

    private IEnumerable<EntityUid> Blueprints(EntityUid uid)
        => TryComp<BlueprintReceiverComponent>(uid, out var receiver) && _containers.TryGetContainer(uid, receiver.ContainerId, out var container)
            ? container.ContainedEntities.Where(item => HasComp<BlueprintComponent>(item)).ToArray() : Array.Empty<EntityUid>();

    private bool IsInventoryContainer(EntityUid uid, string id)
        => TryComp<BlueprintReceiverComponent>(uid, out var receiver) && receiver.ContainerId == id ||
           HasComp<ChemMasterComponent>(uid) && id is SharedChemMaster.InputSlotName or SharedChemMaster.OutputSlotName;

    private MachineStorageSnapshot Capture(EntityUid uid)
    {
        var snapshot = new MachineStorageSnapshot();
        foreach (var blueprint in Blueprints(uid))
        {
            var meta = MetaData(blueprint);
            snapshot.Blueprints.Add(new MachineStoredBlueprint
            {
                Prototype = meta.EntityPrototype?.ID ?? "ResearchRandomBlueprint",
                Name = Name(blueprint),
                Blueprint = _serialization.CreateCopy(Comp<BlueprintComponent>(blueprint), notNullableOverride: true),
            });
        }
        if (HasComp<ChemMasterComponent>(uid) && _solutions.TryGetSolution(uid, SharedChemMaster.BufferSolutionName, out _, out var solution))
            snapshot.Chemicals = solution.Clone();
        return snapshot;
    }

    private void Restore(Entity<PersistentMachineComponent> ent, MachineStorageSnapshot snapshot)
    {
        if (snapshot.Version != 1) throw new InvalidDataException("Unsupported machine storage version.");
        foreach (var item in snapshot.Blueprints)
            if (!_prototypes.HasIndex<EntityPrototype>(item.Prototype)) throw new InvalidDataException($"Unknown stored blueprint prototype {item.Prototype}.");
        if (TryComp<ChemMasterComponent>(ent, out var chem))
            foreach (var id in snapshot.Chemicals.Contents.Select(reagent => reagent.Reagent.Prototype).Distinct())
                if (snapshot.Chemicals.GetTotalPrototypeQuantity(id) > chem.MaxReagentVolume)
                    throw new InvalidDataException($"Stored reagent {id} exceeds this machine's capacity.");

        ent.Comp.Restoring = true;
        try
        {
            foreach (var blueprint in Blueprints(ent).ToArray()) Del(blueprint);
            if (TryComp<BlueprintReceiverComponent>(ent, out var receiver))
            {
                var container = _containers.GetContainer(ent, receiver.ContainerId);
                foreach (var item in snapshot.Blueprints)
                {
                    var uid = EntityManager.CreateEntityUninitialized(item.Prototype, Transform(ent).Coordinates);
                    try
                    {
                        EntityManager.AddComponent(uid, _serialization.CreateCopy(item.Blueprint, notNullableOverride: true), overwrite: true);
                        EntityManager.InitializeAndStartEntity(uid);
                        _metadata.SetEntityName(uid, item.Name);
                        if (!_containers.Insert(uid, container)) throw new InvalidDataException("Could not restore a stored blueprint into its machine.");
                    }
                    catch { if (Exists(uid)) Del(uid); throw; }
                }
            }
            if (chem != null && _solutions.TryGetSolution(ent.Owner, SharedChemMaster.BufferSolutionName, out var solution, out var buffer))
            {
                buffer.RemoveAllSolution();
                buffer.MaxVolume = Content.Shared.FixedPoint.FixedPoint2.MaxValue;
                buffer.CanReact = false;
                buffer.AddSolution(snapshot.Chemicals, _prototypes);
                _solutions.UpdateChemicals(solution.Value, needsReactionsProcessing: false);
            }
        }
        finally { ent.Comp.Restoring = false; }
    }

    public Task SaveNow(EntityUid uid)
    {
        if (!TryComp<PersistentMachineComponent>(uid, out var comp) || !comp.Ready || comp.Closing || comp.Restoring || TerminatingOrDeleted(uid))
            return Task.CompletedTask;
        try
        {
            using var writer = new StringWriter();
            // DataNode.Write preserves polymorphic !type tags (DNA, disease data, etc.).
            _serialization.WriteValue(Capture(uid), alwaysWrite: true, notNullableOverride: true).Write(writer);
            var payload = writer.ToString();
            if (comp.LastPayload == payload) return _writes.GetValueOrDefault(comp.StorageId) ?? Task.CompletedTask;
            comp.LastPayload = payload;
            _writes.TryGetValue(comp.StorageId, out var previous);
            return _writes[comp.StorageId] = SaveAfter(previous, uid, comp.StorageId, comp.Kind, payload);
        }
        catch (Exception error)
        {
            comp.Failed = true;
            comp.LastPayload = null;
            _pending[uid] = _timing.CurTime + TimeSpan.FromSeconds(3);
            Log.Error($"Failed serializing persistent machine '{comp.StorageId}': {error}");
            return Task.CompletedTask;
        }
    }

    private async Task SaveAfter(Task? previous, EntityUid uid, string key, string kind, string payload)
    {
        try
        {
            if (previous != null) await previous.ConfigureAwait(false);
            await _db.SaveMachineStorageAsync(key, kind, payload).ConfigureAwait(false);
            _tasks.RunOnMainThread(() => { if (TryComp<PersistentMachineComponent>(uid, out var comp)) comp.Failed = false; });
        }
        catch (Exception error)
        {
            Log.Error($"Failed saving persistent machine '{key}': {error}");
            _tasks.RunOnMainThread(() =>
            {
                if (!TryComp<PersistentMachineComponent>(uid, out var comp) || comp.Closing) return;
                comp.Failed = true;
                comp.LastPayload = null;
                _pending[uid] = _timing.CurTime + TimeSpan.FromSeconds(3);
            });
        }
    }

    private void Changed(Entity<PersistentMachineComponent> ent)
    {
        if (ent.Comp.Ready && !ent.Comp.Restoring && !ent.Comp.Closing && !TerminatingOrDeleted(ent))
            _ = SaveNow(ent);
    }

    private void OnInserted(Entity<PersistentMachineComponent> ent, ref EntInsertedIntoContainerMessage args) => Changed(ent);
    private void OnRemoved(Entity<PersistentMachineComponent> ent, ref EntRemovedFromContainerMessage args) => Changed(ent);
    private void OnSolutionChanged(Entity<PersistentMachineComponent> ent, ref SolutionContainerChangedEvent args)
    { if (args.SolutionId == SharedChemMaster.BufferSolutionName) Changed(ent); }

    public override void Update(float frameTime)
    {
        foreach (var (uid, time) in _pending.ToArray())
            if (_timing.CurTime >= time)
            {
                _pending.Remove(uid);
                _ = SaveNow(uid);
            }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        var query = EntityQueryEnumerator<PersistentMachineComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            _ = SaveNow(uid);
            comp.Closing = true;
        }
        _pending.Clear();
        _claims.Clear();
    }

    private void OnDestroyed(EntityUid uid, PersistentMachineComponent comp, DestructionEventArgs args)
    {
        if (!comp.Ready || comp.Closing) return;
        comp.LastPayload = null;
        comp.Restoring = true;
        // Destruction drops real contents; the next instance must not recreate those items.
        var empty = new MachineStorageSnapshot();
        using var writer = new StringWriter();
        _serialization.WriteValue(empty, alwaysWrite: true, notNullableOverride: true).Write(writer);
        _writes.TryGetValue(comp.StorageId, out var previous);
        _writes[comp.StorageId] = SaveAfter(previous, uid, comp.StorageId, comp.Kind, writer.ToString());
        comp.Closing = true;
    }

    private void OnShutdown(Entity<PersistentMachineComponent> ent, ref ComponentShutdown args)
    {
        ent.Comp.Closing = true;
        _pending.Remove(ent);
        if (_claims.GetValueOrDefault(ent.Comp.StorageId) == ent.Owner) _claims.Remove(ent.Comp.StorageId);
    }
}

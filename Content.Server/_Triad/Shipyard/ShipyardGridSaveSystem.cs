using System.IO;
using Content.Server.Construction.Components;
using Content.Server.Spreader;
using Content.Server._HL.Shipyard;
using Content.Shared._HL.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.DeviceLinking;
using Content.Shared.DeviceLinking.Components;
using Content.Shared.Mind.Components;
using Content.Shared.Mind;
using Content.Shared.Lathe;
using Content.Shared.Materials;
using Robust.Shared.Network;
using Content.Shared.Wall;
using Robust.Shared.Containers;
using Robust.Shared.EntitySerialization;
using Robust.Shared.Map.Components;
using Robust.Shared.Physics;
using Robust.Shared.Physics.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Serialization;
using Robust.Shared.Serialization.Markdown.Mapping;
using YamlDotNet.Core;
using YamlDotNet.RepresentationModel;
using Content.Shared.Light.Components;
using Content.Shared._Triad.Shipyard.Save;
using Content.Shared._Triad.Shipyard.Load;
using System.Linq;
using Content.Shared.Doors.Components;
using Content.Shared.SprayPainter.Prototypes;
using Content.Shared.Storage;
using Content.Shared.Storage.Components;
using Content.Shared.Materials.OreSilo;
using Content.Shared._radiant.Storage;
using Content.Server._radiant.Storage;
using Content.Server.Station.Systems;
using Content.Server._NF.ShuttleRecords;
using Content.Server.Cargo.Systems;
using Content.Shared._radiant.Shipyard;
using Robust.Shared.Configuration;
using Content.Shared.Xenoarchaeology.Artifact.Components;
using Content.Shared.Station.Components;
using Robust.Shared.IoC;
using Robust.Shared.Serialization.Markdown.Sequence;
using Robust.Shared.Serialization.Markdown.Value;

namespace Content.Server._Triad.Shipyard;

/// <summary>
/// System for saving ships using the MapLoaderSystem infrastructure.
/// Saves ships as complete YAML files similar to savegrid command,
/// after cleaning them of problematic components and moving to exports folder.
/// Update Resources/ServerInfo/_radiant/Guidebook/ShipSaving.xml when changing persistence rules.
/// </summary>
public sealed partial class ShipyardGridSaveSystem : EntitySystem
{
    [Dependency] private IEntityManager _entityManager = default!;
    [Dependency] private IEntitySystemManager _entitySystemManager = default!;
    [Dependency] private PricingSystem _pricing = default!;
    [Dependency] private IConfigurationManager _configManager = default!;
    [Dependency] private SharedContainerSystem _containerSystem = default!;
    [Dependency] private EntityLookupSystem _lookup = default!;
    [Dependency] private IPrototypeManager _prototypeManager = default!;
    [Dependency] private SharedTransformSystem _transform = default!;
    [Dependency] private StationSystem _station = default!;
    [Dependency] private ShuttleRecordsSystem _shuttleRecords = default!;
    [Dependency] private ShipSaveSigningSystem _signing = default!;
    [Dependency] private TriadTamperPolicyService _tamperPolicy = default!;
    [Dependency] private readonly IDependencyCollection _dependencies = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedMindSystem _minds = default!;
    [Dependency] private readonly SharedMaterialStorageSystem _materials = default!;

    public List<ShipSaveLimitsPrototype> ShipSaveEntityLimits { get; private set; } = new();

    private ISawmill _sawmill = default!;

    private readonly HashSet<Entity<SpawnOnShipLoadComponent>> _spawnOnShipLoadEntities = new();
    private readonly HashSet<Entity<ShipSaveLimitComponent>> _limitedEntitiesList = new();

    private EntityQuery<MapGridComponent> _gridQuery;
    private EntityQuery<HLPersistOnShipSaveComponent> _persistOnSaveQuery;
    private EntityQuery<TransformComponent> _transformQuery;
    [Dependency] private Robust.Shared.Timing.IGameTiming _timing = default!;
    private readonly Dictionary<Guid, PendingSave> _pendingSaves = new();
    private sealed record PendingSave(EntityUid Grid, EntityUid Deed, ICommonSession Session, string CharacterName,
        AuthenticatedShipFile File, TimeSpan Expires, Action<bool> Completed);

    public override void Initialize()
    {
        base.Initialize();

        _gridQuery = GetEntityQuery<MapGridComponent>();
        _persistOnSaveQuery = GetEntityQuery<HLPersistOnShipSaveComponent>();
        _transformQuery = GetEntityQuery<TransformComponent>();

        // Initialize sawmill for logging
        _sawmill = Logger.GetSawmill("shipyard.gridsave");

        ShipSaveEntityLimits = GetSaveShipEntityLimits(_prototypeManager);
        SubscribeNetworkEvent<ShipSaveWrittenMessage>(OnSaveWritten);
    }

    /// <summary>
    /// Tries to save a ship, remove the given deed's deed component, and clean up the grid after saving.
    /// </summary>
    public bool TrySaveShip(EntityUid grid, EntityUid deedUid, ICommonSession playerSession, Action<bool> completed)
    {
        if (!TryComp<ShuttleDeedComponent>(deedUid, out var deed))
        {
            _sawmill.Warning($"Player {playerSession.Name} tried to save ship with invalid deed UID: {deedUid}");
            return false;
        }

        var shipName = deed.ShuttleName ?? "Unknown_Ship";
        if (playerSession.AttachedEntity is not { } player)
            return false;
        var characterName = GetOwnerCharacterName(player);
        if (string.IsNullOrWhiteSpace(characterName)
            || TryComp<SavedShipComponent>(grid, out var previousSave) && previousSave.HasOwner
                && !previousSave.IsOwner(playerSession.UserId, characterName))
            return false;
        if (deed.ShuttleUid != grid || _pendingSaves.Values.Any(save => save.Grid == grid))
            return false;

        _sawmill.Info($"Trying to save {playerSession.Name} ship {shipName}");

        try
        {
            if (!TryBuildShipSaveYaml(grid, out var yaml, out var appraisal, playerSession.UserId, characterName))
                return false;
            var file = _signing.Sign(yaml, appraisal);
            var payload = file.ShipFileString();
            var request = Guid.NewGuid();
            _pendingSaves.Add(request, new(grid, deedUid, playerSession, characterName, file,
                _timing.RealTime + TimeSpan.FromSeconds(60), completed));
            RaiseNetworkEvent(new SendShipSaveDataClientMessage(shipName, payload, request), playerSession);
            return true;
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Ship save failed: {exception}");
            return false;
        }
    }

    private void OnSaveWritten(ShipSaveWrittenMessage message, EntitySessionEventArgs args)
    {
        if (!_pendingSaves.TryGetValue(message.RequestId, out var save)
            || save.Session != args.SenderSession)
            return;
        _pendingSaves.Remove(message.RequestId);
        if (!message.Success || Deleted(save.Grid) || EntityManager.IsQueuedForDeletion(save.Grid)
            || save.Session.AttachedEntity is not { } player
            || GetOwnerCharacterName(player) != save.CharacterName
            || !TryComp<ShuttleDeedComponent>(save.Deed, out var deed) || deed.ShuttleUid != save.Grid
            || _entitySystemManager.GetEntitySystem<Content.Server._NF.Shipyard.Systems.ShipyardSystem>()
                .FoundOrganics(save.Grid, GetEntityQuery<Content.Shared.Mobs.Components.MobStateComponent>(), _transformQuery) != null)
        {
            _sawmill.Warning($"Ship save confirmation rejected for {save.Grid}; client write succeeded: {message.Success}.");
            save.Completed(false);
            return;
        }
        var station = _station.GetOwningStation(save.Grid);
        var replacedPath = TryComp<DeployedShipSaveComponent>(save.Grid, out var deployment)
            && deployment.Loader == save.Session.UserId ? deployment.SourceFilePath : null;
        try
        {
            _signing.MakeAvailable(save.File);
            QueueDel(save.Grid);
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Unable to commit ship save: {exception}");
            try
            {
                _signing.Consume(save.File);
            }
            catch (Exception cleanupException)
            {
                _sawmill.Error($"Unable to revoke failed ship save: {cleanupException}");
            }
            save.Completed(false);
            return;
        }

        _tamperPolicy.RecordSave(save.File, save.Session, Name(save.Grid));

        // The file and grid removal are committed. A bookkeeping error is not a failed save.
        try
        {
            if (!string.IsNullOrWhiteSpace(replacedPath))
                RaiseNetworkEvent(new DeleteLocalShipFileMessage(replacedPath), save.Session);
            RemoveAllShuttleDeeds(save.Grid);
            if (station is { } owningStation)
            {
                if (TryComp<StationDataComponent>(owningStation, out var data) && data.Grids.Count > 1)
                    _station.RemoveGridFromStation(owningStation, save.Grid);
                else
                    _station.DeleteStation(owningStation);
            }
            _shuttleRecords.RefreshStateForAll(true);
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Ship saved, but post-save bookkeeping failed: {exception}");
        }
        save.Completed(true);
    }

    public override void Update(float frameTime)
    {
        foreach (var (request, save) in _pendingSaves.ToArray())
        {
            if (_timing.RealTime < save.Expires)
                continue;
            _pendingSaves.Remove(request);
            _sawmill.Warning($"Ship save {request} for {save.Grid} timed out waiting for the client file write.");
            save.Completed(false);
        }
    }

    public override void Shutdown()
    {
        _pendingSaves.Clear();
        base.Shutdown();
    }

    /// <summary>
    /// Removes all ShuttleDeedComponents that reference the specified shuttle EntityUid
    /// </summary>
    public void RemoveAllShuttleDeeds(EntityUid shuttleUid)
    {
        var query = _entityManager.EntityQueryEnumerator<ShuttleDeedComponent>();
        var deedsToRemove = new List<EntityUid>();

        while (query.MoveNext(out var entityUid, out var deed))
        {
            if (deed.ShuttleUid != null && Exists(deed.ShuttleUid.Value) && deed.ShuttleUid.Value == shuttleUid)
            {
                deedsToRemove.Add(entityUid);
            }
        }

        foreach (var deedEntity in deedsToRemove)
        {
            RemComp<ShuttleDeedComponent>(deedEntity);
        }
    }

    /// <summary>
    /// Goes through a grid and checks for any entities with a SpawnOnShipLoadComponent.
    /// </summary>
    public void CreateSpawnOnShipLoadEntities(EntityUid gridUid)
    {
        if (!_gridQuery.HasComp(gridUid))
            return;

        var toDelete = new HashSet<EntityUid>();

        _spawnOnShipLoadEntities.Clear();

        // Get the entities on the grid with the ship save limit comp
        var gridTransform = _transformQuery.GetComponent(gridUid);
        var worldAABB = _lookup.GetWorldAABB(gridUid, gridTransform);
        _lookup.GetEntitiesIntersecting(gridTransform.MapID, worldAABB, _spawnOnShipLoadEntities);

        foreach ((var ent, var comp) in _spawnOnShipLoadEntities)
        {
            if (ent == gridUid)
                continue;
            if (!_transformQuery.TryComp(ent, out var entXform) || entXform.GridUid != gridUid)
                continue;

            var position = _transform.GetMoverCoordinates(ent);
            var newEntity = Spawn(comp.Spawn, position);
            _transform.AttachToGridOrMap(newEntity);

            if (comp.DeleteSelfAfterSpawn)
                toDelete.Add(ent);
        }

        foreach (var uid in toDelete)
        {
            QueueDel(uid);
        }
    }

    /// <summary>
    /// Checks if this grid obeys the limits for certain entities
    /// </summary>
    public bool CheckGridEntityLimits(EntityUid gridUid, out string message)
    {
        message = string.Empty;

        if (!_gridQuery.HasComp(gridUid))
            return false;

        _limitedEntitiesList.Clear();

        var entityAmount = new Dictionary<string, int>();

        // Get the entities on the grid with the ship save limit comp
        var gridTransform = _transformQuery.GetComponent(gridUid);
        var worldAABB = _lookup.GetWorldAABB(gridUid, gridTransform);
        _lookup.GetEntitiesIntersecting(gridTransform.MapID, worldAABB, _limitedEntitiesList);

        foreach ((var ent, var limit) in _limitedEntitiesList)
        {
            if (ent == gridUid)
                continue;

            if (!_transformQuery.TryComp(ent, out var entXForm) || entXForm.GridUid != gridUid)
                continue;

            var limitId = limit.LimitId;
            entityAmount.TryGetValue(limitId, out var count);
            entityAmount[limitId] = count + 1;
        }

        var obeysLimit = true;

        foreach (var (id, amount) in entityAmount)
        {
            foreach (var limitProto in ShipSaveEntityLimits)
            {
                if (!limitProto.Limits.TryGetValue(id, out var max))
                    continue;

                var limitIdLoc = Loc.GetString("shipyard-grid-save-limit-" + id);
                var messagePart = Loc.GetString("shipyard-grid-save-limit-message", ("id", limitIdLoc), ("max", max));

                if (amount > max)
                {
                    message += $"{messagePart}\n";
                    obeysLimit = false;
                }
            }
        }

        return obeysLimit;
    }

    public static List<ShipSaveLimitsPrototype> GetSaveShipEntityLimits(IPrototypeManager prototypeManager)
    {
        return prototypeManager
            .EnumeratePrototypes<ShipSaveLimitsPrototype>()
            .ToList();
    }

    /// <summary>
    /// The serialization options every ship save runs with. Exposed so tests exercise the options the
    /// live save uses rather than a copy that can drift from them.
    /// </summary>
    internal static readonly SerializationOptions ShipSaveSerializationOptions = SerializationOptions.Default with
    {
        // Do NOT auto-include referenced entities (players/admin observers/etc.).
        // This prevents exceptions when encountering unserializable entities and keeps saves scoped to the grid.
        MissingEntityBehaviour = MissingEntityBehaviour.Ignore,
        ErrorOnOrphan = false,
        // Disable auto-include logging to avoid excessive log spam/lag during saves.
        LogAutoInclude = null
    };

    /// <summary>
    /// Serializes a filtered snapshot without deleting entities or changing live components.
    /// Signing, client delivery and removal of the original grid happen separately.
    /// </summary>
    /// <returns>False only when <paramref name="gridUid"/> is not a grid. Serialization failures throw.</returns>
    internal bool TryBuildShipSaveYaml(
        EntityUid gridUid,
        [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out string? yaml,
        out int appraisalCost,
        NetUserId? owner = null,
        string? characterName = null)
    {
        yaml = null;
        appraisalCost = 0;

        if (!_gridQuery.HasComp(gridUid))
            return false;

        var loadShipPrice = Math.Clamp(_configManager.GetCVar(ShipSaveCVars.LoadShipPrice), 0f, 1f);
        var fullAppraisal = _pricing.AppraiseGrid(gridUid, null);
        appraisalCost = (int)MathF.Round((float)fullAppraisal * loadShipPrice);

        // MapLoader's BeforeSerializationEvent handlers can mutate live device links and followers.
        // Use the engine serializer directly, with snapshot-local filters and writers instead.
        var serializer = new EntitySerializer(_dependencies, ShipSaveSerializationOptions);
        serializer.SerializerProvider.RegisterSerializer(new ShipSaveSerializationWriters());
        serializer.OnIsSerializeable += FilterShipSaveEntity;
        serializer.SerializeEntityRecursive(gridUid);

        var safeContents = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (_, (uid, data)) in serializer.EntityData)
        {
            PrepareSnapshotEntity(uid, data);
            if (uid == gridUid && owner is { } userId)
            {
                if (string.IsNullOrWhiteSpace(characterName))
                    throw new InvalidDataException("A character name is required for a bound ship save.");
                var saved = SnapshotComponent((SequenceDataNode) data["components"], "SavedShip");
                saved["ownerUserId"] = new ValueDataNode(userId.ToString());
                saved["ownerCharacterName"] = new ValueDataNode(characterName.Trim());
            }
            if (IsInsidePasswordSafe(uid) && data.TryGet("uid", out ValueDataNode? savedUid))
                safeContents.Add(savedUid.Value);
        }

        var node = serializer.Write();

        // 2) Sanitize the node to match blueprint conventions
        ShipSaveYamlSanitizer.SanitizeShipSaveNode(node, _prototypeManager, safeContents);

        // 3) Convert MappingDataNode to YAML text without touching disk
        yaml = WriteYamlToString(node);
        return true;
    }

    private void FilterShipSaveEntity(Entity<MetaDataComponent> entity, ref bool serializable)
    {
        if (IsInvalidEntity(entity.Owner) || HasComp<EdgeSpreaderComponent>(entity.Owner))
        {
            serializable = false;
            return;
        }
        // The engine otherwise repairs this inconsistent transform while serializing it.
        if (_transformQuery.TryComp(entity.Owner, out var xform) && xform.NoLocalRotation && xform.LocalRotation != 0)
            throw new InvalidDataException($"Ship entity {ToPrettyString(entity.Owner)} has a rotated no-rotation transform.");
    }

    private void PrepareSnapshotEntity(EntityUid uid, MappingDataNode data)
    {
        if (!data.TryGet("components", out SequenceDataNode? components))
            return;

        foreach (var node in components.ToArray())
        {
            if (node is not MappingDataNode component
                || !component.TryGet("type", out ValueDataNode? type))
                continue;

            switch (type.Value)
            {
                case "SpreaderGrid":
                case "LatheProducing":
                    components.Remove(node);
                    break;
                case "GeneralStationRecordConsole":
                    component["activeKey"] = ValueDataNode.Null();
                    component["filter"] = ValueDataNode.Null();
                    break;
            }
        }

        // These components can have no serialized overrides, so add explicit snapshot overrides.
        if (ShouldClearSavedMaterials(uid) && HasComp<MaterialStorageComponent>(uid))
            SnapshotComponent(components, "MaterialStorage")["storage"] = new MappingDataNode();

        if (HasComp<PoweredLightComponent>(uid))
            SnapshotComponent(components, "PoweredLight")["hasLampOnSpawn"] = ValueDataNode.Null();

        // Runtime appearance data is not a DataField. Export only the selected paint style.
        if (TryComp<AppearanceComponent>(uid, out var appearance)
            && _appearance.TryGetData<string>(uid, PaintableVisuals.Prototype, out var paintedPrototype, appearance))
            ShipSaveYamlSanitizer.ApplyPaintStyleToAppearance(
                SnapshotComponent(components, "Appearance"), paintedPrototype);

        if (!HasComp<MapGridComponent>(uid))
            return;

        var saved = SnapshotComponent(components, "SavedShip");
        saved["saveId"] = new ValueDataNode(Guid.NewGuid().ToString());
        if (_station.GetOwningStation(uid) is { } station
            && TryComp<Content.Server._NF.Station.Components.ExtraShuttleInformationComponent>(station, out var info)
            && info.Vessel is { } vessel)
            saved["vesselId"] = new ValueDataNode(vessel.Id);
    }

    internal string GetOwnerCharacterName(EntityUid player)
        => (_minds.TryGetMind(player, out _, out var mind) ? mind.CharacterName ?? Name(player) : Name(player)).Trim();

    private bool ShouldClearSavedMaterials(EntityUid uid)
        => HasComp<LatheComponent>(uid) || HasComp<OreSiloComponent>(uid);

    internal void ClearLoadedMachineMaterials(EntityUid grid)
    {
        var query = EntityQueryEnumerator<MaterialStorageComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var storage, out var xform))
        {
            if (xform.GridUid != grid || !ShouldClearSavedMaterials(uid))
                continue;
            foreach (var (material, amount) in _materials.GetStoredMaterials((uid, storage), localOnly: true))
            {
                if (amount != 0 && !_materials.TryChangeMaterialAmount(uid, material.Id, -amount, storage, localOnly: true))
                    throw new InvalidDataException($"Unable to clear saved machine materials on {ToPrettyString(uid)}.");
            }
        }
    }

    private static MappingDataNode SnapshotComponent(SequenceDataNode components, string name)
    {
        foreach (var node in components)
        {
            if (node is MappingDataNode component && component.TryGet("type", out ValueDataNode? type)
                && type.Value == name)
                return component;
        }
        var added = new MappingDataNode { { "type", new ValueDataNode(name) } };
        components.Add(added);
        return added;
    }


    /// <summary>
    /// Checks whether an entity should be excluded from the snapshot. Never deletes it.
    /// </summary>
    private bool IsInvalidEntity(EntityUid uid)
    {
        if (!Exists(uid))
            return false;
        if (IdentityDocumentPersistence.IsDocument(EntityManager, uid))
            return true;
        // Skip if terminating
        if (_entityManager.GetComponent<MetaDataComponent>(uid).EntityLifeStage >= EntityLifeStage.Terminating)
            return false;
        if (!IsInsidePasswordSafe(uid) && IsInsideEmptySavedStorage(uid))
            return true;
        if (HasComp<MindContainerComponent>(uid))
            return true; // do not save things with minds
        if (_persistOnSaveQuery.HasComp(uid))
            return false; // preserve explicitly marked entities
        if (_gridQuery.HasComp(uid))
            return false; // never delete grid root or nested grids here
        // Preserve artifact nodes: the artifact's NodeVertices names each one
        if (HasComp<XenoArtifactNodeComponent>(uid))
            return false;
        // Preserve wall-mounted fixtures (buttons, posters, etc.) regardless of anchored state
        if (HasComp<WallMountComponent>(uid))
            return false;
        // Preserve levers
        if (HasComp<TwoWayLeverComponent>(uid))
            return false;
        // Preserve entities with static body types, such as drains or sinks.
        if (TryComp<PhysicsComponent>(uid, out var physics) && physics.BodyType == BodyType.Static)
            return false;
        // Preserve solutions
        if (HasComp<ContainedSolutionComponent>(uid) || HasComp<SolutionComponent>(uid))
            return false;
        // Save anchored entities
        if (_transformQuery.TryComp(uid, out var xform) && xform.Anchored)
            return false;

        var inContainer = _containerSystem.IsEntityInContainer(uid);
        if (inContainer)
        {
            // If this entity (at any ancestor depth) is ultimately inside a secret stash preserve it.
            if (IsInsidePersistentStorage(uid))
                return false;
        }

        // Only unanchored entities are eligible for deletion. If it's unanchored (loose) or unanchored-in-container, delete.
        return true;
    }

    private bool IsInsidePasswordSafe(EntityUid uid)
    {
        var current = uid;
        for (var depth = 0; depth < 64 && _containerSystem.TryGetContainingContainer(current, out var container); depth++)
        {
            current = container.Owner;
            if (HasComp<PasswordSafeComponent>(current))
                return true;
        }
        return false;
    }

    private bool IsInsideEmptySavedStorage(EntityUid uid)
    {
        var current = uid;
        for (var depth = 0; depth < 64 && _containerSystem.TryGetContainingContainer(current, out var container); depth++)
        {
            current = container.Owner;
            // Explicitly empty furniture overrides even an item's own persistence marker.
            if ((HasComp<StorageComponent>(current) || HasComp<EntityStorageComponent>(current))
                && _persistOnSaveQuery.TryComp(current, out var persist) && !persist.SaveContents)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Returns true if the given entity is contained in a storage that is considered persistent, such as a machine or ship stash.
    /// </summary>
    private bool IsInsidePersistentStorage(EntityUid ent)
    {
        // Fast path: immediately contained?
        if (!_containerSystem.IsEntityInContainer(ent))
            return false;

        EntityUid current = ent;
        var safety = 0;
        while (safety++ < 64 && _containerSystem.TryGetContainingContainer(current, out var container))
        {
            var owner = container.Owner;
            if (!Exists(owner))
                return false;
            // Also treat persistent entities as a preservation root.
            if (_persistOnSaveQuery.TryComp(owner, out var persist) && persist.SaveContents)
                return true; // Found stash root above.
            if (HasComp<MachineComponent>(owner))
                return true; // This is so machines keep their upgraded parts.
            if (HasComp<AirlockComponent>(owner))
                return true; // Preserve installed electronics even on prefilled airlocks.
            if (HasComp<PoweredLightComponent>(owner))
                return true;
            current = owner;
        }
        return false;
    }


    private static string WriteYamlToString(MappingDataNode node)
    {
        // Based on MapLoaderSystem.Write but to a string instead of file
        var document = new YamlDocument(node.ToYaml());
        using var writer = new StringWriter();
        var stream = new YamlStream { document };
        stream.Save(new YamlMappingFix(new Emitter(writer)), false);
        return writer.ToString();
    }
}

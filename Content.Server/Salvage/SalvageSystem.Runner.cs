using System.Numerics;
using System.Diagnostics.CodeAnalysis; //Radiant Sector
using Content.Server.Ghost.Roles.Components; //Radiant Sector
using Content.Server.Salvage.Expeditions;
using Content.Server.Storage.EntitySystems; //Radiant Sector
using Content.Shared.Implants; //Radiant Sector
using Content.Shared.Implants.Components; //Radiant Sector
using Content.Shared.Radio; //Radiant Sector
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Events;
using Content.Shared.Chat;
using Content.Shared.Humanoid;
using Content.Shared.Mind.Components; //Radiant Sector
using Content.Shared.Mobs.Components;
using Content.Shared.Mobs; //Radiant Sector
using Content.Shared.Mobs.Systems;
using Content.Shared.Salvage.Expeditions;
using Content.Shared.Shuttles.Components;
using Content.Shared.Localizations;
using Content.Shared.Station.Components;
using Content.Shared.Storage.Components; //Radiant Sector
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes; //Radiant Sector
using Robust.Shared.Map; // Frontier
using Content.Server.GameTicking; // Frontier
using Content.Server._NF.Salvage.Expeditions.Structure; // Frontier
using Content.Server._NF.Salvage.Expeditions;
using Content.Shared.Salvage; // Frontier

namespace Content.Server.Salvage;

public sealed partial class SalvageSystem
{
    /*
     * Handles actively running a salvage expedition.
     */

    private static readonly TimeSpan ManualFinishCooldown = TimeSpan.FromMinutes(4.5); ///radiant sector
    private static readonly ProtoId<RadioChannelPrototype> RescueMedicalChannel = "Medical"; ///Radiant Sector
    private const string SeparatistRadioImplantPrototype = "SeparatistsTrackingImplant"; ///Radiant Sector

    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly GameTicker _gameTicker = default!; // Frontier
    [Dependency] private readonly EntityStorageSystem _entityStorage = default!; ///Radiant Sector
    [Dependency] private readonly SharedSubdermalImplantSystem _subdermalImplant = default!; ///Radiant Sector

    private void InitializeRunner()
    {
        SubscribeLocalEvent<FTLRequestEvent>(OnFTLRequest);
        SubscribeLocalEvent<FTLStartedEvent>(OnFTLStarted);
        SubscribeLocalEvent<FTLCompletedEvent>(OnFTLCompleted);
        SubscribeLocalEvent<ConsoleFTLAttemptEvent>(OnConsoleFTLAttempt);
        SubscribeLocalEvent<MobStateChangedEvent>(OnRescueImplantMobStateChanged); ///Radiant Sector
    }

    private void OnConsoleFTLAttempt(ref ConsoleFTLAttemptEvent ev)
    {
        if (!TryComp(ev.Uid, out TransformComponent? xform) ||
            !TryComp<SalvageExpeditionComponent>(xform.MapUid, out var salvage))
        {
            return;
        }

        // TODO: This is terrible but need bluespace harnesses or something.
        var query = EntityQueryEnumerator<HumanoidAppearanceComponent, MobStateComponent, TransformComponent>();

        while (query.MoveNext(out var uid, out _, out var mobState, out var mobXform))
        {
            if (mobXform.MapUid != xform.MapUid)
                continue;

            // Don't count unidentified humans (loot) or anyone you murdered so you can still maroon them once dead.
            if (_mobState.IsDead(uid, mobState))
                continue;

            // Okay they're on salvage, so are they on the shuttle.
            if (mobXform.GridUid != ev.Uid)
            {
                ev.Cancelled = true;
                ev.Reason = Loc.GetString("salvage-expedition-not-all-present");
                return;
            }
        }
    }

    /// <summary>
    /// Announces status updates to salvage crewmembers on the state of the expedition.
    /// </summary>
    private void Announce(EntityUid mapUid, string text)
    {
        var mapId = Comp<MapComponent>(mapUid).MapId;

        // I love TComms and chat!!!
        _chat.ChatMessageToManyFiltered(
            Filter.BroadcastMap(mapId),
            ChatChannel.Radio,
            text,
            text,
            _mapSystem.GetMapOrInvalid(mapId),
            false,
            true,
            null);
    }

    private void OnFTLRequest(ref FTLRequestEvent ev)
    {
        if (!HasComp<SalvageExpeditionComponent>(ev.MapUid) ||
            !TryComp<FTLDestinationComponent>(ev.MapUid, out var dest))
        {
            return;
        }

        // Only one shuttle can occupy an expedition.
        dest.Enabled = false;
        _shuttleConsoles.RefreshShuttleConsoles();
    }

    private void OnFTLCompleted(ref FTLCompletedEvent args)
    {
        if (!TryComp<SalvageExpeditionComponent>(args.MapUid, out var component))
            return;

        // Someone FTLd there so start announcement
        if (component.Stage != ExpeditionStage.Added)
            return;

        // Frontier: early finish
        if (TryComp<SalvageExpeditionDataComponent>(component.Station, out var data))
        {
            data.CanFinish = false;
            data.ManualFinishAvailableAt = _timing.CurTime + ManualFinishCooldown;
            UpdateConsoles((component.Station, data));
        }
        // End Frontier: early finish

        Announce(args.MapUid, Loc.GetString("salvage-expedition-announcement-countdown-minutes", ("duration", (component.EndTime - _timing.CurTime).Minutes)));

        var directionLocalization = ContentLocalizationManager.FormatDirection(component.DungeonLocation.GetDir()).ToLower();

        if (component.DungeonLocation != Vector2.Zero)
            Announce(args.MapUid, Loc.GetString("salvage-expedition-announcement-dungeon", ("direction", directionLocalization)));

        // Frontier: type-specific announcement
        switch (component.MissionParams.MissionType)
        {
            case SalvageMissionType.Destruction:
                if (TryComp<SalvageDestructionExpeditionComponent>(args.MapUid, out var destruction)
                    && destruction.Structures.Count > 0
                    && TryComp(destruction.Structures[0], out MetaDataComponent? structureMeta)
                    && structureMeta.EntityPrototype != null)
                {
                    var name = structureMeta.EntityPrototype.Name;
                    if (string.IsNullOrWhiteSpace(name))
                        name = Loc.GetString("salvage-expedition-announcement-destruction-entity-fallback");
                    // Assuming all structures are of the same type.
                    Announce(args.MapUid, Loc.GetString("salvage-expedition-announcement-destruction", ("structure", name), ("count", destruction.Structures.Count)));
                }
                break;
            case SalvageMissionType.Elimination:
                if (TryComp<SalvageEliminationExpeditionComponent>(args.MapUid, out var elimination)
                    && elimination.Megafauna.Count > 0
                    && TryComp(elimination.Megafauna[0], out MetaDataComponent? targetMeta)
                    && targetMeta.EntityPrototype != null)
                {
                    var name = targetMeta.EntityPrototype.Name;
                    if (string.IsNullOrWhiteSpace(name))
                        name = Loc.GetString("salvage-expedition-announcement-elimination-entity-fallback");
                    // Assuming all megafauna are of the same type.
                    Announce(args.MapUid, Loc.GetString("salvage-expedition-announcement-elimination", ("target", name), ("count", elimination.Megafauna.Count)));
                }
                break;
            default:
                break; // No announcement
        }
        // End Frontier

        component.Stage = ExpeditionStage.Running;
        Dirty(args.MapUid, component);
    }

    private void OnFTLStarted(ref FTLStartedEvent ev)
    {
        if (!TryComp<SalvageExpeditionComponent>(ev.FromMapUid, out var expedition) ||
            !TryComp<SalvageExpeditionDataComponent>(expedition.Station, out var station))
        {
            return;
        }

        station.CanFinish = false; // Frontier
        station.ManualFinishAvailableAt = TimeSpan.Zero; // Frontier

        // Check if any shuttles remain.
        var query = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();

        while (query.MoveNext(out _, out var xform))
        {
            if (xform.MapUid == ev.FromMapUid)
                return;
        }

        // Last shuttle has left so finish the mission.
        QueueDel(ev.FromMapUid.Value);
    }

    // Runs the expedition
    private void UpdateRunner()
    {
        UpdateRescueImplants(); ///Radiant Sector

        // Generic missions
        var query = EntityQueryEnumerator<SalvageExpeditionComponent>();

        // Run the basic mission timers (e.g. announcements, auto-FTL, completion, etc)
        while (query.MoveNext(out var uid, out var comp))
        {
            var remaining = comp.EndTime - _timing.CurTime;
            var audioLength = _audio.GetAudioLength(comp.SelectedSong);

            if (!comp.RescueStarted && ///Radiant Sector
                comp.Stage >= ExpeditionStage.Running && ///Radiant Sector
                remaining <= comp.RescueTimeBeforeEnd && ///Radiant Sector
                remaining > TimeSpan.Zero) ///Radiant Sector
            {
                comp.RescueStarted = true; ///Radiant Sector
                Dirty(uid, comp); ///Radiant Sector
                RescueExpeditionCasualties(uid, comp); ///Radiant Sector
            }

            if (comp.Stage < ExpeditionStage.FinalCountdown && remaining < TimeSpan.FromSeconds(45))
            {
                comp.Stage = ExpeditionStage.FinalCountdown;
                Dirty(uid, comp);
                Announce(uid, Loc.GetString("salvage-expedition-announcement-countdown-seconds", ("duration", TimeSpan.FromSeconds(45).Seconds)));
            }
            else if (comp.Stage < ExpeditionStage.MusicCountdown && remaining < audioLength) // Frontier
            {
                // Frontier: handled client-side.
                // var audio = _audio.PlayPvs(comp.Sound, uid);
                // comp.Stream = audio?.Entity;
                // _audio.SetMapAudio(audio);
                // End Frontier
                comp.Stage = ExpeditionStage.MusicCountdown;
                Dirty(uid, comp);
                Announce(uid, Loc.GetString("salvage-expedition-announcement-countdown-minutes", ("duration", audioLength.Minutes)));
            }
            else if (comp.Stage < ExpeditionStage.Countdown && remaining < TimeSpan.FromMinutes(5)) // Frontier: 4<5
            {
                comp.Stage = ExpeditionStage.Countdown;
                Dirty(uid, comp);
                Announce(uid, Loc.GetString("salvage-expedition-announcement-countdown-minutes", ("duration", TimeSpan.FromMinutes(5).Minutes)));
            }
            // Auto-FTL out any shuttles
            else if (remaining < TimeSpan.FromSeconds(_shuttle.DefaultStartupTime) + TimeSpan.FromSeconds(0.5))
            {
                var ftlTime = (float)remaining.TotalSeconds;

                if (remaining < TimeSpan.FromSeconds(_shuttle.DefaultStartupTime))
                {
                    ftlTime = MathF.Max(0, (float)remaining.TotalSeconds - 0.5f);
                }

                ftlTime = MathF.Min(ftlTime, _shuttle.DefaultStartupTime);
                var shuttleQuery = AllEntityQuery<ShuttleComponent, TransformComponent>();

                if (TryComp<StationDataComponent>(comp.Station, out var data))
                {
                    foreach (var member in data.Grids)
                    {
                        while (shuttleQuery.MoveNext(out var shuttleUid, out var shuttle, out var shuttleXform))
                        {
                            if (shuttleXform.MapUid != uid || HasComp<FTLComponent>(shuttleUid))
                                continue;

                            // Frontier: try to find a potential destination for ship that doesn't collide with other grids.
                            var mapId = _gameTicker.DefaultMap;
                            if (!_mapSystem.TryGetMap(mapId, out var mapUid))
                            {
                                Log.Error($"Could not get DefaultMap EntityUID, shuttle {shuttleUid} may be stuck on expedition.");
                                continue;
                            }

                            // Destination generator parameters (move to CVAR?)
                            int numRetries = 20; // Maximum number of retries
                            float minDistance = 200f; // Minimum distance from another object, in meters
                            float minRange = 750f; // Minimum distance from sector centre, in meters
                            float maxRange = 3500f; // Maximum distance from sector centre, in meters

                            // Get a list of all grid positions on the destination map
                            List<Vector2> gridCoords = new();
                            var gridQuery = EntityManager.AllEntityQueryEnumerator<MapGridComponent, TransformComponent>();
                            while (gridQuery.MoveNext(out var _, out _, out var xform))
                            {
                                if (xform.MapID == mapId)
                                    gridCoords.Add(_transform.GetWorldPosition(xform));
                            }

                            Vector2 dropLocation = _random.NextVector2(minRange, maxRange);
                            for (int i = 0; i < numRetries; i++)
                            {
                                bool positionIsValid = true;
                                foreach (var station in gridCoords)
                                {
                                    if (Vector2.Distance(station, dropLocation) < minDistance)
                                    {
                                        positionIsValid = false;
                                        break;
                                    }
                                }

                                if (positionIsValid)
                                    break;

                                // No good position yet, pick another random position.
                                dropLocation = _random.NextVector2(minRange, maxRange);
                            }

                            _shuttle.FTLToCoordinates(shuttleUid, shuttle, new EntityCoordinates(mapUid.Value, dropLocation), 0f, ftlTime, TravelTime);
                            // End Frontier:  try to find a potential destination for ship that doesn't collide with other grids.
                            //_shuttle.FTLToDock(shuttleUid, shuttle, member, ftlTime); // Frontier: use above instead
                        }

                        break;
                    }
                }
            }

            if (remaining < TimeSpan.Zero)
            {
                QueueDel(uid);
            }
        }

        var finishCooldownQuery = EntityQueryEnumerator<SalvageExpeditionComponent>();
        while (finishCooldownQuery.MoveNext(out _, out var expedition))
        {
            if (expedition.Stage < ExpeditionStage.Running ||
                !TryComp<SalvageExpeditionDataComponent>(expedition.Station, out var data) ||
                data.CanFinish ||
                data.ManualFinishAvailableAt == TimeSpan.Zero ||
                data.ManualFinishAvailableAt > _timing.CurTime)
            {
                continue;
            }

            data.CanFinish = true;
            data.ManualFinishAvailableAt = TimeSpan.Zero;
            UpdateConsoles((expedition.Station, data));
        }

        // Frontier: mission-specific logic
        // Destruction
        var structureQuery = EntityQueryEnumerator<SalvageDestructionExpeditionComponent, SalvageExpeditionComponent>();

        while (structureQuery.MoveNext(out var uid, out var structure, out var comp))
        {
            if (comp.Completed)
                continue;

            var structureAnnounce = false;

            for (var i = structure.Structures.Count - 1; i >= 0; i--)
            {
                var objective = structure.Structures[i];

                if (Deleted(objective))
                {
                    structure.Structures.RemoveAt(i);
                    structureAnnounce = true;
                }
            }

            if (structureAnnounce)
                Announce(uid, Loc.GetString("salvage-expedition-structure-remaining", ("count", structure.Structures.Count)));

            if (structure.Structures.Count == 0)
            {
                comp.Completed = true;
                Announce(uid, Loc.GetString("salvage-expedition-completed"));
            }
        }

        // Elimination
        var eliminationQuery = EntityQueryEnumerator<SalvageEliminationExpeditionComponent, SalvageExpeditionComponent>();
        while (eliminationQuery.MoveNext(out var uid, out var elimination, out var comp))
        {
            if (comp.Completed)
                continue;

            var announce = false;

            for (var i = elimination.Megafauna.Count - 1; i >= 0; i--)
            {
                var mob = elimination.Megafauna[i];

                if (Deleted(mob) || _mobState.IsDead(mob))
                {
                    elimination.Megafauna.RemoveAt(i);
                    announce = true;
                }
            }

            if (announce)
                Announce(uid, Loc.GetString("salvage-expedition-megafauna-remaining", ("count", elimination.Megafauna.Count)));

            if (elimination.Megafauna.Count == 0)
            {
                comp.Completed = true;
                Announce(uid, Loc.GetString("salvage-expedition-completed"));
            }
        }
        // End Frontier: mission-specific logic
    }

    private void RescueExpeditionCasualties(EntityUid expeditionMap, SalvageExpeditionComponent expedition) ///Radiant Sector start
    {
        var shuttles = new List<EntityUid>();
        var shuttleQuery = EntityQueryEnumerator<ShuttleComponent, TransformComponent>();

        while (shuttleQuery.MoveNext(out var shuttleUid, out _, out var shuttleXform))
        {
            if (shuttleXform.MapUid == expeditionMap && !HasComp<FTLComponent>(shuttleUid))
                shuttles.Add(shuttleUid);
        }

        if (shuttles.Count == 0)
            return;

        var rescuedByShuttle = new Dictionary<EntityUid, List<string>>();
        var rescueSourceByShuttle = new Dictionary<EntityUid, EntityUid>();
        var playerQuery = EntityQueryEnumerator<MindContainerComponent, MobStateComponent, TransformComponent>();

        while (playerQuery.MoveNext(out var playerUid, out var mind, out var mobState, out var playerXform))
        {
            if (!mind.HasMind ||
                IsGhostRoleEntity(playerUid) ||
                playerXform.MapUid != expeditionMap ||
                mobState.CurrentState is not (MobState.Critical or MobState.Dead))
            {
                continue;
            }

            if (!TryGetRescuePod(shuttles, expedition, out var pod, out var podStorage, out var podShuttle))
                continue;

            var implantUid = EnsureRescueImplant(playerUid, expedition, Name(podShuttle), mobState);

            if (podStorage.Open)
                _entityStorage.CloseStorage(pod, podStorage);

            if (!_entityStorage.Insert(playerUid, pod, podStorage))
                continue;

            if (!rescuedByShuttle.TryGetValue(podShuttle, out var rescued))
            {
                rescued = new List<string>();
                rescuedByShuttle[podShuttle] = rescued;
            }

            rescued.Add(Loc.GetString(
                "salvage-expedition-rescue-patient-entry",
                ("patient", Name(playerUid)),
                ("coordinates", GetRescueCoordinatesText(playerUid))));

            if (implantUid != null)
                rescueSourceByShuttle.TryAdd(podShuttle, implantUid.Value);
        }

        foreach (var (shuttle, rescued) in rescuedByShuttle)
        {
            var source = rescueSourceByShuttle.GetValueOrDefault(shuttle, shuttle);
            var message = Loc.GetString(
                "salvage-expedition-rescue-medical-radio",
                ("shuttle", Name(shuttle)),
                ("patients", string.Join(", ", rescued)),
                ("coordinates", GetRescueCoordinatesText(shuttle)));

            _radioSystem.SendRadioMessage(source, message, RescueMedicalChannel, shuttle);
        }
    }

    private bool TryGetRescuePod(
        IReadOnlyList<EntityUid> shuttles,
        SalvageExpeditionComponent expedition,
        out EntityUid pod,
        [NotNullWhen(true)] out EntityStorageComponent? storage,
        out EntityUid podShuttle)
    {
        foreach (var shuttle in shuttles)
        {
            var podQuery = EntityQueryEnumerator<EntityStorageComponent, MetaDataComponent, TransformComponent>();

            while (podQuery.MoveNext(out var podUid, out var podStorage, out var meta, out var podXform))
            {
                if (podXform.GridUid != shuttle ||
                    meta.EntityPrototype?.ID != expedition.RescuePodPrototype.Id ||
                    podStorage.Contents.ContainedEntities.Count > 0)
                {
                    continue;
                }

                pod = podUid;
                storage = podStorage;
                podShuttle = shuttle;
                return true;
            }
        }

        var targetShuttle = shuttles[0];
        var offset = GetRescuePodSpawnOffset(targetShuttle, expedition.RescuePodPrototype);
        var spawned = Spawn(expedition.RescuePodPrototype, new EntityCoordinates(targetShuttle, offset));

        if (TryComp(spawned, out storage))
        {
            pod = spawned;
            podShuttle = targetShuttle;
            return true;
        }

        QueueDel(spawned);
        pod = default;
        storage = null;
        podShuttle = default;
        return false;
    }

    private Vector2 GetRescuePodSpawnOffset(EntityUid shuttle, EntProtoId rescuePodPrototype)
    {
        var count = 0;
        var podQuery = EntityQueryEnumerator<MetaDataComponent, TransformComponent>();

        while (podQuery.MoveNext(out _, out var meta, out var xform))
        {
            if (xform.GridUid == shuttle && meta.EntityPrototype?.ID == rescuePodPrototype.Id)
                count++;
        }

        return new Vector2(count % 4, count / 4);
    }

    private EntityUid? EnsureRescueImplant(
        EntityUid target,
        SalvageExpeditionComponent expedition,
        string shuttleName,
        MobStateComponent? mobState = null)
    {
        if (HasImplantPrototype(target, SeparatistRadioImplantPrototype))
            return null;

        if (TryComp<ImplantedComponent>(target, out var implanted))
        {
            foreach (var implant in implanted.ImplantContainer.ContainedEntities)
            {
                if (!TryComp<ExpeditionRescueMedicalImplantComponent>(implant, out var rescueImplant))
                    continue;

                SetupRescueImplant(target, rescueImplant, expedition.RescueImplantDelay, shuttleName, mobState);
                rescueImplant.Activated = false;
                Dirty(implant, rescueImplant);
                return implant;
            }
        }

        var implantUid = _subdermalImplant.AddImplant(target, expedition.RescueImplantPrototype);
        if (implantUid != null &&
            TryComp<ExpeditionRescueMedicalImplantComponent>(implantUid.Value, out var component))
        {
            SetupRescueImplant(target, component, expedition.RescueImplantDelay, shuttleName, mobState);
            Dirty(implantUid.Value, component);
        }

        return implantUid;
    }

    private void SetupRescueImplant(
        EntityUid target,
        ExpeditionRescueMedicalImplantComponent rescueImplant,
        TimeSpan delay,
        string shuttleName,
        MobStateComponent? mobState = null)
    {
        rescueImplant.AlertDelay = delay;
        rescueImplant.PatientName = Name(target);
        rescueImplant.ShuttleName = shuttleName;
        rescueImplant.ActivateAt = Resolve(target, ref mobState, false) && mobState.CurrentState == MobState.Dead
            ? _timing.CurTime + delay
            : TimeSpan.Zero;
    }

    private void OnRescueImplantMobStateChanged(MobStateChangedEvent args)
    {
        if (args.NewMobState != MobState.Dead ||
            HasImplantPrototype(args.Target, SeparatistRadioImplantPrototype) ||
            !TryComp<ImplantedComponent>(args.Target, out var implanted))
        {
            return;
        }

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (!TryComp<ExpeditionRescueMedicalImplantComponent>(implant, out var rescueImplant) ||
                rescueImplant.Activated)
            {
                continue;
            }

            rescueImplant.PatientName = string.IsNullOrWhiteSpace(rescueImplant.PatientName)
                ? Name(args.Target)
                : rescueImplant.PatientName;
            rescueImplant.ActivateAt = _timing.CurTime + rescueImplant.AlertDelay;
            Dirty(implant, rescueImplant);
        }
    }

    private void UpdateRescueImplants()
    {
        var query = EntityQueryEnumerator<ExpeditionRescueMedicalImplantComponent, SubdermalImplantComponent>();

        while (query.MoveNext(out var implantUid, out var rescueImplant, out var subdermal))
        {
            if (rescueImplant.Activated ||
                rescueImplant.ActivateAt == TimeSpan.Zero ||
                rescueImplant.ActivateAt > _timing.CurTime ||
                subdermal.ImplantedEntity == null ||
                Deleted(subdermal.ImplantedEntity.Value))
            {
                continue;
            }

            var target = subdermal.ImplantedEntity.Value;
            if (HasImplantPrototype(target, SeparatistRadioImplantPrototype))
            {
                rescueImplant.Activated = true;
                Dirty(implantUid, rescueImplant);
                continue;
            }

            if (!TryComp<MobStateComponent>(target, out var mobState) ||
                mobState.CurrentState != MobState.Dead)
            {
                rescueImplant.ActivateAt = TimeSpan.Zero;
                Dirty(implantUid, rescueImplant);
                continue;
            }

            var message = Loc.GetString(
                "salvage-expedition-rescue-implant-medical-radio",
                ("patient", string.IsNullOrWhiteSpace(rescueImplant.PatientName) ? Name(target) : rescueImplant.PatientName),
                ("shuttle", string.IsNullOrWhiteSpace(rescueImplant.ShuttleName) ? Loc.GetString("salvage-expedition-rescue-unknown-shuttle") : rescueImplant.ShuttleName),
                ("coordinates", GetRescueCoordinatesText(target)));

            _radioSystem.SendRadioMessage(implantUid, message, RescueMedicalChannel, target);

            rescueImplant.Activated = true;
            Dirty(implantUid, rescueImplant);
        }
    }

    private string GetRescueCoordinatesText(EntityUid target)
    {
        var coordinates = _transform.GetMapCoordinates(target);
        return $"({(int) coordinates.X}, {(int) coordinates.Y})"; ///Radiant Sector end
    }

    private bool IsGhostRoleEntity(EntityUid uid)
    {
        return HasComp<GhostRoleComponent>(uid) ||
               HasComp<GhostTakeoverAvailableComponent>(uid);
    }

    private bool HasImplantPrototype(EntityUid target, string prototype)
    {
        if (!TryComp<ImplantedComponent>(target, out var implanted))
            return false;

        foreach (var implant in implanted.ImplantContainer.ContainedEntities)
        {
            if (MetaData(implant).EntityPrototype?.ID == prototype)
                return true;
        }

        return false;
    }
}

using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Server.Station.Components;
using Content.Server.Station.Events;
using Content.Server.Station.Systems;
using Content.Server.StationRecords.Components;
using Content.Shared._NF.Roles.Components;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._radiant.Careers;
using Content.Shared.Examine;
using Content.Shared.GameTicking;
using Content.Shared.Station.Components;
using Content.Shared.UserInterface;
using Content.Shared.Popups;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Careers;

public sealed class CareerFleetSystem : EntitySystem
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly StationSystem _stations = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    private static readonly TimeSpan Grace = TimeSpan.FromMinutes(5);
    private CareerSystem Careers => EntityManager.System<CareerSystem>();
    private StationJobsSystem Jobs => EntityManager.System<StationJobsSystem>();

    public bool IsFleetJob(string profession, string job) =>
        _prototypes.TryIndex<Content.Shared._radiant.Careers.CareerProfessionPrototype>(profession, out var definition)
        && (definition.Jobs.Contains(job) || definition.InterviewJob?.Id == job);

    private string EntryJob(string profession) =>
        _prototypes.Index<Content.Shared._radiant.Careers.CareerProfessionPrototype>(profession).Jobs[0];

    public override void Initialize()
    {
        SubscribeLocalEvent<PlayerSpawnCompleteEvent>(OnSpawn);
        SubscribeLocalEvent<ShipyardConsoleComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<ShipyardConsoleComponent, ActivatableUIOpenAttemptEvent>(OnShipyardOpen);
        SubscribeLocalEvent<CareerFleetComponent, StationPostInitEvent>(OnFleetInitialized);
        SubscribeLocalEvent<CareerFleetComponent, StationGridAddedEvent>((uid, comp, args) => ApplyRadarAppearance(args.GridId, comp.Profession));
    }

    private void OnFleetInitialized(Entity<CareerFleetComponent> ent, ref StationPostInitEvent args)
    {
        foreach (var grid in args.Station.Comp.Grids)
            ApplyRadarAppearance(grid, ent.Comp.Profession);
    }

    private void ApplyRadarAppearance(EntityUid grid, string profession)
    {
        if (!_prototypes.TryIndex<CareerProfessionPrototype>(profession, out var definition) || definition.RadarColor == null)
            return;
        var radar = EnsureComp<CareerRadarComponent>(grid);
        if (radar.Profession.Id == profession)
            return;
        radar.Profession = profession;
        Dirty(grid, radar);
    }

    public bool CanUseShipyard(ICommonSession session, EntityUid console, out string? reason, bool forPurchase = true)
    {
        reason = null;
        var profession = Careers.GetProfession(session);
        var consoleId = MetaData(console).EntityPrototype?.ID;
        foreach (var definition in _prototypes.EnumeratePrototypes<Content.Shared._radiant.Careers.CareerProfessionPrototype>())
        {
            if (definition.ExclusiveShipyard is not { } shipyard)
                continue;
            if (consoleId == shipyard.Id && profession != definition.ID)
            {
                reason = Loc.GetString("career-fleet-shipyard-restricted", ("profession", Loc.GetString(definition.Name)));
                return false;
            }
            if (forPurchase && profession == definition.ID && consoleId != shipyard.Id)
            {
                reason = Loc.GetString("career-fleet-use-exclusive-shipyard");
                return false;
            }
        }
        return true;
    }

    private void OnShipyardOpen(EntityUid uid, ShipyardConsoleComponent component, ActivatableUIOpenAttemptEvent args)
    {
        string? reason = null;
        if (_players.TryGetSessionByEntity(args.User, out var session) && CanUseShipyard(session, uid, out reason, forPurchase: false))
            return;
        args.Cancel();
        if (reason != null)
            _popup.PopupEntity(reason, uid, args.User);
    }

    private bool Online(NetUserId user) => _players.TryGetSessionById(user, out var session)
        && session.Status != SessionStatus.Disconnected;

    private bool Powered(EntityUid station, CareerFleetComponent fleet)
    {
        var query = EntityQueryEnumerator<GeneralStationRecordConsoleComponent, TransformComponent>();
        while (query.MoveNext(out var console, out _, out var xform))
        {
            if ((xform.GridUid == fleet.Grid || _stations.GetOwningStation(console) == station)
                && TryComp<ApcPowerReceiverComponent>(console, out var receiver) && receiver.Powered)
                return true;
        }
        return false;
    }

    private bool Refresh(EntityUid station, CareerFleetComponent fleet)
    {
        var now = _timing.CurTime;
        fleet.OfflineSince = fleet.Participants.Keys.Any(Online) ? null : fleet.OfflineSince ?? now;
        fleet.UnpoweredSince = Powered(station, fleet) ? null : fleet.UnpoweredSince ?? now;
        var enabled = Exists(fleet.Grid) && !TerminatingOrDeleted(fleet.Grid)
            && (fleet.OfflineSince == null || now - fleet.OfflineSince.Value < Grace)
            && (fleet.UnpoweredSince == null || now - fleet.UnpoweredSince.Value < Grace);
        if (enabled != fleet.SpawnEnabled)
        {
            fleet.SpawnEnabled = enabled;
            Jobs.UpdateJobsAvailable();
        }
        return enabled;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<CareerFleetComponent>();
        while (query.MoveNext(out var station, out var fleet))
            Refresh(station, fleet);
    }

    public bool IsSpawnEnabled(EntityUid station)
    {
        if (TryComp<CareerFleetComponent>(station, out var fleet))
            return Refresh(station, fleet);
        // A career hull without a registered crew has no late-join destination.
        return !TryComp<StationDataComponent>(station, out var data)
            || !data.Grids.Any(grid => HasComp<CareerFleetGridComponent>(grid));
    }

    /// <summary>Capacity independent of the console-editable manifest; the buyer reserves one seat.</summary>
    public int GetOpenCrewSlots(EntityUid station, CareerFleetComponent fleet)
    {
        var occupied = 1;
        fleet.Participants.TryGetValue(fleet.Buyer, out var buyer);
        var query = EntityQueryEnumerator<JobTrackingComponent>();
        while (query.MoveNext(out var entity, out var job))
        {
            if (entity != buyer && job.Active && job.SpawnStation == station && job.Job is { } role && IsFleetJob(fleet.Profession, role.Id))
                occupied++;
        }
        return Math.Max(0, 3 - occupied);
    }

    public (int Active, int Limit) GetFleetCount(string profession)
    {
        var workers = new HashSet<NetUserId>();
        var actors = EntityQueryEnumerator<CareerActorComponent>();
        while (actors.MoveNext(out var entity, out var actor))
        {
            if (Online(actor.User) && _players.TryGetSessionById(actor.User, out var worker)
                && worker.AttachedEntity == entity && Careers.TryGetCareer(entity, out var career)
                && career.Profession == profession && Careers.IsWorking(entity))
                workers.Add(actor.User);
        }
        var active = 0;
        var query = EntityQueryEnumerator<CareerFleetComponent>();
        while (query.MoveNext(out var station, out var fleet))
        {
            if (fleet.Profession == profession && Refresh(station, fleet))
                active++;
        }
        return (active, Math.Max(1, (workers.Count + 2) / 3));
    }

    /// <summary>Finds one active enrolled crew whose vessel tier is strictly lower than the purchase.</summary>
    public bool TryGetUpgrade(ICommonSession session, VesselPrototype vessel, out EntityUid station)
    {
        station = default;
        if (vessel.CareerProfession == null || vessel.CareerFree || vessel.Price <= 0
            || Careers.GetProfession(session) != vessel.CareerProfession)
            return false;
        var found = false;
        var highestLevel = int.MinValue;
        var query = EntityQueryEnumerator<CareerFleetComponent>();
        while (query.MoveNext(out var candidate, out var fleet))
        {
            if (fleet.Profession != vessel.CareerProfession || !fleet.Participants.ContainsKey(session.UserId)
                || !Refresh(candidate, fleet)
                || !TryComp<CareerFleetGridComponent>(fleet.Grid, out var hull)
                || !_prototypes.TryIndex<VesselPrototype>(hull.Vessel, out var previous)
                || previous.CareerProfession != vessel.CareerProfession
                || previous.CareerLevel >= vessel.CareerLevel || previous.CareerLevel <= highestLevel)
                continue;
            station = candidate;
            highestLevel = previous.CareerLevel;
            found = true;
        }
        return found;
    }

    public bool CanPurchase(ICommonSession session, VesselPrototype vessel, out string? reason, bool allowReplacement = true)
    {
        reason = null;
        var profession = Careers.GetProfession(session);
        if (vessel.CareerProfession == null)
        {
            if (profession == "Adventurer")
                return true;
            reason = Loc.GetString("career-fleet-profession-required");
            return false;
        }
        if (!_prototypes.TryIndex<Content.Shared._radiant.Careers.CareerProfessionPrototype>(vessel.CareerProfession, out var definition)
            || definition.Jobs.Count == 0 || profession != vessel.CareerProfession
            || session.AttachedEntity is not { } player || !Careers.TryGetCareer(player, out var career)
            || career.Profession != profession || Careers.GetLevel(player) < vessel.CareerLevel)
        {
            reason = Loc.GetString("career-fleet-level-required", ("level", vessel.CareerLevel));
            return false;
        }
        var (active, limit) = GetFleetCount(profession);
        if (active < limit || (allowReplacement && active == limit && TryGetUpgrade(session, vessel, out _)))
            return true;
        reason = Loc.GetString("career-fleet-limit", ("active", active), ("limit", limit));
        return false;
    }

    public bool CanJoin(ICommonSession session, EntityUid station, string job, out string? reason)
    {
        reason = null;
        if (!Careers.IsJobAllowed(session, job))
        {
            reason = Loc.GetString("career-fleet-profession-required");
            return false;
        }
        if (!IsSpawnEnabled(station))
        {
            reason = Loc.GetString("career-fleet-unavailable");
            return false;
        }
        var profession = Careers.GetProfession(session);
        if (TryComp<CareerFleetComponent>(station, out var fleet))
        {
            if (!Refresh(station, fleet) || profession != fleet.Profession || !IsFleetJob(profession, job))
            {
                reason = Loc.GetString("career-fleet-unavailable");
                return false;
            }
            if (fleet.Buyer == session.UserId)
                return true;
            if (GetOpenCrewSlots(station, fleet) == 0
                || !Jobs.TryGetJobSlot(station, job, out var slots) || slots == 0)
            {
                reason = Loc.GetString("career-fleet-full");
                return false;
            }
            return true;
        }
        if (profession != "Adventurer")
        {
            // Civilian captains may hire a professional through their advertised interview slot.
            if (_prototypes.TryIndex<Content.Shared._radiant.Careers.CareerProfessionPrototype>(profession, out var definition)
                && definition.InterviewJob is { } interview && IsFleetJob(profession, job)
                && HasComp<StationJobsComponent>(station) && Jobs.TryGetJobSlot(station, interview, out _))
                return true;
            if (!TryComp<CareerBaseStationComponent>(station, out var careerBase) || !careerBase.Allows(profession))
            {
                reason = Loc.GetString("career-fleet-unavailable");
                return false;
            }
            var query = EntityQueryEnumerator<CareerFleetComponent>();
            while (query.MoveNext(out var other, out var crew))
            {
                if (crew.Profession == profession && Refresh(other, crew)
                    && GetOpenCrewSlots(other, crew) > 0
                    && Jobs.TryGetJobSlot(other, job, out var slots) && slots != 0)
                {
                    reason = Loc.GetString("career-fleet-join-crew");
                    return false;
                }
            }
        }
        return true;
    }

    public void Register(EntityUid station, EntityUid grid, EntityUid buyer, ICommonSession session, VesselPrototype vessel,
        bool allowReplacement = true)
    {
        if (vessel.CareerProfession == null)
            return;
        CareerFleetComponent? previous = null;
        EntityUid previousStation = default;
        if (allowReplacement && TryGetUpgrade(session, vessel, out previousStation) && previousStation != station)
            TryComp(previousStation, out previous);

        // Runtime enrollment is rebuilt on deployment, never trusted from a save file.
        var fleet = EnsureComp<CareerFleetComponent>(station);
        fleet.Grid = grid;
        fleet.Buyer = session.UserId;
        fleet.Profession = vessel.CareerProfession;
        fleet.Participants = previous == null ? new() : new(previous.Participants);
        fleet.Participants[session.UserId] = buyer;
        fleet.OfflineSince = null;
        fleet.UnpoweredSince = null;
        fleet.SpawnEnabled = true;
        EnsureComp<CareerFleetGridComponent>(grid).Vessel = vessel.ID;
        ApplyRadarAppearance(grid, vessel.CareerProfession);
        RemComp<ShipOwnershipComponent>(grid);

        if (previous != null)
        {
            previous.Participants.TryGetValue(previous.Buyer, out var previousBuyer);
            EntityManager.System<GameTicker>().TransferCareerFleetJobs(previousStation, station, previousBuyer, EntryJob(fleet.Profession));
            if (HasComp<StationJobsComponent>(previousStation))
            {
                foreach (var job in Jobs.GetJobs(previousStation).Keys.ToArray())
                    Jobs.TrySetJobSlot(previousStation, job, 0);
            }
            foreach (var participant in previous.Participants.Keys)
                Jobs.TryRemovePlayerJobs(previousStation, participant);
            RemComp<CareerFleetComponent>(previousStation);
        }

        if (HasComp<StationJobsComponent>(station))
        {
            foreach (var job in Jobs.GetJobs(station).Keys.ToArray())
                Jobs.TrySetJobSlot(station, job, 0);
            var entryJob = EntryJob(fleet.Profession);
            Jobs.TrySetJobSlot(station, entryJob, 3, createSlot: true);
            Jobs.TryAssignJob(station, entryJob, session.UserId);
            var assigned = new HashSet<NetUserId> { session.UserId };
            var tracking = EntityQueryEnumerator<JobTrackingComponent, CareerActorComponent>();
            while (tracking.MoveNext(out _, out var job, out var actor))
            {
                if (job.Active && job.SpawnStation == station && job.Job?.Id == entryJob
                    && fleet.Participants.ContainsKey(actor.User) && assigned.Add(actor.User))
                    Jobs.TryAssignJob(station, entryJob, actor.User);
            }
            Jobs.TrySetJobSlot(station, entryJob, GetOpenCrewSlots(station, fleet));
        }
        Refresh(station, fleet);
        Jobs.UpdateJobsAvailable();
    }

    private void OnSpawn(PlayerSpawnCompleteEvent args)
    {
        if (HasComp<InterviewHologramComponent>(args.Mob)
            || !TryComp<CareerFleetComponent>(args.Station, out var fleet)
            || (Transform(args.Mob).GridUid != fleet.Grid && _stations.GetOwningStation(args.Mob) != args.Station))
            return;
        fleet.Participants[args.Player.UserId] = args.Mob;
        Refresh(args.Station, fleet);
    }

    /// <summary>Online enrolled crew entities, including the caller, deduplicated across their fleets.</summary>
    public HashSet<EntityUid> GetParticipants(EntityUid player)
    {
        var result = new HashSet<EntityUid>();
        NetUserId? user = TryComp<CareerActorComponent>(player, out var actor) ? actor.User : null;
        if (user == null && _players.TryGetSessionByEntity(player, out var session))
            user = session.UserId;
        if (user == null)
            return result;
        var query = EntityQueryEnumerator<CareerFleetComponent>();
        while (query.MoveNext(out _, out var fleet))
        {
            if (!fleet.Participants.ContainsKey(user.Value))
                continue;
            foreach (var participant in fleet.Participants.Keys)
            {
                if (Online(participant) && _players.TryGetSessionById(participant, out var member)
                    && member.AttachedEntity is { } entity && HasComp<CareerActorComponent>(entity)
                    && !TerminatingOrDeleted(entity))
                    result.Add(entity);
            }
        }
        return result;
    }

    private void OnExamine(EntityUid uid, ShipyardConsoleComponent component, ExaminedEvent args)
    {
        if (!_players.TryGetSessionByEntity(args.Examiner, out var session)
            || Careers.GetProfession(session) == "Adventurer")
            return;
        var (active, limit) = GetFleetCount(Careers.GetProfession(session));
        args.PushMarkup(Loc.GetString("career-fleet-count", ("active", active), ("limit", limit),
            ("available", Math.Max(0, limit - active))));
    }
}

// Content.Server/_radiant/GridWanted/Systems/GridWantedSystem.cs
using Content.Server.CriminalRecords.Systems;
using Content.Server.Mind;
using Content.Server.Radio.EntitySystems;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Server._radiant.GridWanted;
using Content.Shared.CriminalRecords;
using Content.Shared.CriminalRecords.Components;
using Content.Shared.CriminalRecords.Systems;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Content.Shared.Ghost;
using Robust.Shared.Timing;
// logging and popup removed

namespace Content.Server._radiant.GridWanted.Systems;

public sealed class GridWantedSystem : EntitySystem
{
    [Dependency] private readonly CriminalRecordsSystem _criminalRecords = default!;
    [Dependency] private readonly StationRecordsSystem _stationRecords = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    private float _checkTimer = 0f;

    public override void Initialize()
    {
        base.Initialize();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        _checkTimer += frameTime;
        if (_checkTimer < 2.0f)
            return;

        _checkTimer = 0f;

        ProcessAllZones();
    }

    private void ProcessAllZones()
    {
        var zoneQuery = EntityQueryEnumerator<DangerZoneComponent>();

        while (zoneQuery.MoveNext(out var gridUid, out var zone))
        {
            ProcessZone(gridUid, zone.Reason);
        }
    }

    private void ProcessZone(EntityUid gridUid, string reason)
    {
        var zoneQuery = EntityQueryEnumerator<DangerZoneComponent>();
        DangerZoneComponent? zone = null;
        while (zoneQuery.MoveNext(out var uid, out var z))
        {
            if (uid == gridUid)
            {
                zone = z;
                break;
            }
        }
        if (zone == null) return;

        var currentPlayers = new HashSet<EntityUid>();

        // Только для игроков
        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } playerUid)
                continue;

            // Ignore ghosts and aghosts (visiting entities)
            if (HasComp<GhostComponent>(playerUid))
                continue;

            // Check if mind is visiting another entity (aghost)
            if (_mind.TryGetMind(playerUid, out var mindId, out var mind))
            {
                if (mind.VisitingEntity is { Valid: true })
                    continue;
            }

            var transform = Transform(playerUid);
            if (transform.GridUid != gridUid)
                continue;

            currentPlayers.Add(playerUid);
            if (!zone.PreviousPlayers.Contains(playerUid))
            {
                string playerName = Name(playerUid);
                // Вошел на грид, проверяем кулдаун и был ли уже wanted
                if (!zone.AlreadyWantedPlayers.Contains(playerName))
                {
                    var currentTime = _timing.CurTime;
                    if (!zone.LastWantedNotification.TryGetValue(playerName, out var lastTime) || (currentTime - lastTime).TotalMinutes >= 15)
                    {
                        // Ставим розыск
                        SetCriminalRecordWanted(playerUid, reason, zone);
                        zone.LastWantedNotification[playerName] = currentTime;
                    }
                }
            }
        }

        zone.PreviousPlayers = currentPlayers;
    }

    private void SetCriminalRecordWanted(EntityUid playerUid, string reason, DangerZoneComponent zone)
    {
        try
        {
            // processing player

            // 1. Получаем StationRecordKey игрока
            if (!TryGetPlayerRecordKey(playerUid, out var recordKey))
                return;

            // Проверяем что recordKey не null
            if (recordKey == null)
                return;

            // found record

            // 2. Получаем CriminalRecord
            if (!_stationRecords.TryGetRecord<CriminalRecord>(recordKey.Value, out var record))
                return;

            bool wasWanted = record.Status == SecurityStatus.Wanted;

            // 3. Ставим розыск через OverwriteStatus (всегда устанавливает)
            string officer = "Система автоматического наблюдения";
            _criminalRecords.OverwriteStatus(recordKey.Value, record, SecurityStatus.Wanted, reason, officer);

            zone.AlreadyWantedPlayers.Add(Name(playerUid));

            if (!wasWanted)
            {
                // Send radio message
                SendRadioWantedMessage(playerUid, reason, officer);
            }

            // wanted set

            // Обновляем иконку через Shared систему
            UpdateCriminalIcon(playerUid);
        }
        catch (Exception)
        {
            // swallow
        }
    }

    private void UpdateCriminalIcon(EntityUid playerUid)
    {
        if (EntityManager.System<SharedCriminalRecordsSystem>() is { } sharedSystem)
        {
            sharedSystem.SetCriminalIcon(
                Name(playerUid),
                SecurityStatus.Wanted,
                playerUid
            );
        }
    }

    private bool TryGetPlayerRecordKey(EntityUid playerUid, out StationRecordKey? recordKey)
    {
        recordKey = null;

        string playerName = Name(playerUid);

        var stationQuery = EntityQueryEnumerator<StationRecordsComponent>();

        while (stationQuery.MoveNext(out var stationUid, out _))
        {
            var recordId = _stationRecords.GetRecordByName(stationUid, playerName);
            if (recordId.HasValue)
            {
                recordKey = new StationRecordKey(recordId.Value, stationUid);
                return true;
            }
        }
        return false;
    }

    private void SendRadioWantedMessage(EntityUid playerUid, string reason, string officer)
    {
        string playerName = Name(playerUid);

        // Find a virtual entity to send message from (like other systems do)
        // We'll use the first criminal records console if available, or create virtual sender
        var consoleQuery = EntityQueryEnumerator<CriminalRecordsConsoleComponent>();
        EntityUid? sender = null;

        while (consoleQuery.MoveNext(out var uid, out _))
        {
            sender = uid;
            break;
        }

        if (sender == null)
            return;

        // Format message like CriminalRecordsConsoleSystem does
        var message = Loc.GetString("criminal-records-console-wanted",
            ("name", playerName),
            ("officer", officer),
            ("reason", reason),
            ("job", "Unknown"));

        _radio.SendRadioMessage(sender.Value, message, "Nfsd", sender.Value);
    }
}

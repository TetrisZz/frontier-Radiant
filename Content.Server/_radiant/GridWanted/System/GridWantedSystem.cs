// Content.Server/_radiant/GridWanted/Systems/GridWantedSystem.cs
using Content.Server.CriminalRecords.Systems;
using Content.Server.Mind;
using Content.Server.Radio.EntitySystems;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Systems;
using Content.Server._radiant.GridWanted;
using Content.Shared.Access.Systems;
using Content.Shared.CriminalRecords;
using Content.Shared.CriminalRecords.Components;
using Content.Shared.CriminalRecords.Systems;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Robust.Server.GameObjects;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Content.Shared.Ghost;
using Robust.Shared.Timing;
using Robust.Shared.Map;
using System.Linq;
using Content.Shared.GameTicking;
using Robust.Shared.Player;
using System;
using Robust.Shared.Collections;
using Robust.Shared.Log;
using Content.Server._NF.RoundNotifications.Events;
using Content.Server.Station.Components;
using Content.Server.Station.Systems;
using Robust.Shared.Network; // ДОБАВЬ ЭТО!

namespace Content.Server._radiant.GridWanted.Systems;

public sealed class GridWantedSystem : EntitySystem
{
    [Dependency] private readonly CriminalRecordsSystem _criminalRecords = default!;
    [Dependency] private readonly StationRecordsSystem _stationRecords = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    [Dependency] private readonly MindSystem _mind = default!;
    [Dependency] private readonly RadioSystem _radio = default!;
    [Dependency] private readonly SharedIdCardSystem _idCardSystem = default!;
    [Dependency] private readonly IGameTiming _gameTiming = default!;
    [Dependency] private readonly SharedTransformSystem _transformSystem = default!;
    [Dependency] private readonly SharedGameTicker _gameTicker = default!;
    [Dependency] private readonly StationSystem _stationSystem = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    private ISawmill _sawmill = default!;

    private float _checkTimer = 0f;
    private TimeSpan _roundStartTime;

    // Храним по UserId с временем последнего розыска
    private readonly Dictionary<EntityUid, ZoneRuntimeData> _zoneData = new();

    private sealed class ZoneRuntimeData
    {
        // Храним когда последний раз выдавали розыск (15 минут КД)
        public Dictionary<NetUserId, TimeSpan> PlayerCooldowns { get; set; } = new();
        // Для быстрой проверки уже разысканных в этом раунде
        public HashSet<NetUserId> AlreadyWantedThisRound { get; set; } = new();
    }

    public override void Initialize()
    {
        base.Initialize();

        _sawmill = _logManager.GetSawmill("gridwanted");

        SubscribeLocalEvent<RoundStartedEvent>(OnRoundStart);
        SubscribeLocalEvent<RoundRestartCleanupEvent>(OnRoundRestart);

        SubscribeLocalEvent<DangerZoneComponent, MapInitEvent>(OnMapInit);
    }

    private void OnRoundStart(RoundStartedEvent ev)
    {
        _roundStartTime = _gameTiming.CurTime;
        // НЕ очищаем _zoneData полностью, только очищаем AlreadyWantedThisRound
        foreach (var data in _zoneData.Values)
        {
            data.AlreadyWantedThisRound.Clear();
        }
    }

    private void OnMapInit(EntityUid uid, DangerZoneComponent component, MapInitEvent args)
    {
        if (!_zoneData.ContainsKey(uid))
        {
            _zoneData[uid] = new ZoneRuntimeData();
        }
    }

    private void OnRoundRestart(RoundRestartCleanupEvent args)
    {
        // Очищаем полностью при рестарте
        _zoneData.Clear();
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Ждем 10 секунд после старта раунда
        if (_gameTiming.CurTime < _roundStartTime + TimeSpan.FromSeconds(10))
        {
            _checkTimer = 0f;
            return;
        }

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
            ProcessZone(gridUid, zone);
        }
    }

    // Получение UserId игрока
    private bool TryGetPlayerUserId(EntityUid playerUid, out NetUserId userId)
    {
        userId = default;

        if (!_playerManager.TryGetSessionByEntity(playerUid, out var session))
            return false;

        userId = session.UserId;
        return true;
    }

    private void ProcessZone(EntityUid gridUid, DangerZoneComponent zone)
    {
        if (!_zoneData.TryGetValue(gridUid, out var zoneRuntimeData))
        {
            zoneRuntimeData = new ZoneRuntimeData();
            _zoneData[gridUid] = zoneRuntimeData;
        }

        var currentTime = _gameTiming.CurTime;

        foreach (var session in _playerManager.Sessions)
        {
            if (session.AttachedEntity is not { } playerUid)
                continue;

            if (HasComp<GhostComponent>(playerUid))
                continue;

            if (!TryComp(playerUid, out TransformComponent? transform))
                continue;

            if (transform.GridUid != gridUid)
                continue;

            // Получаем UserId (уникальный на аккаунт)
            if (!TryGetPlayerUserId(playerUid, out var userId))
                continue;

            // Получаем имя для записи в розыск
            if (!TryGetPlayerName(playerUid, out var playerName))
                continue;

            // ПРОВЕРКА КД 15 МИНУТ
            if (zoneRuntimeData.PlayerCooldowns.TryGetValue(userId, out var lastWantedTime))
            {
                var timeSinceLastWanted = currentTime - lastWantedTime;

                // Если прошло меньше 15 минут - пропускаем
                if (timeSinceLastWanted.TotalMinutes < 15.0)
                {
                    continue;
                }
            }

            // Проверяем, не разыскивается ли уже в этом раунде
            if (zoneRuntimeData.AlreadyWantedThisRound.Contains(userId))
                continue;

            // Выдаем розыск (без проверки IsPlayerAlreadyWanted)
            if (TrySetCriminalRecordWanted(playerUid, zone.Reason, playerName))
            {
                // УСПЕШНО: обновляем данные
                zoneRuntimeData.PlayerCooldowns[userId] = currentTime;
                zoneRuntimeData.AlreadyWantedThisRound.Add(userId);

                _sawmill.Info($"Выдан розыск игроку {playerName} (UserId: {userId}) по причине: {zone.Reason}. КД: 15 минут");
            }
        }
    }

    // Возвращает bool (успешно или нет)
    private bool TrySetCriminalRecordWanted(EntityUid playerUid, string reason, string playerName)
    {
        try
        {
            if (!TryGetPlayerRecordKey(playerName, out var recordKey))
                return false;

            if (recordKey == null)
                return false;

            if (!_stationRecords.TryGetRecord<CriminalRecord>(recordKey.Value, out var record))
                return false;

            // Если уже в розыске - не трогаем
            if (record.Status == SecurityStatus.Wanted)
                return false;

            string officer = "Система автоматического наблюдения";
            string playerJobTitle = GetRecordedJobTitle(recordKey.Value) ?? GetPlayerJob(playerUid) ?? "Unknown";

            // Выдаем розыск
            _criminalRecords.OverwriteStatus(recordKey.Value, record, SecurityStatus.Wanted, reason, officer);

            // Отправляем радио-сообщение
            SendRadioWantedMessage(playerUid, reason, officer, playerJobTitle);

            // Обновляем иконку
            UpdateCriminalIcon(playerUid);

            return true;
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Error setting wanted status for {playerName}: {ex}");
            return false;
        }
    }

    private void UpdateCriminalIcon(EntityUid playerUid)
    {
        try
        {
            if (!TryGetPlayerName(playerUid, out var playerName))
                return;

            if (EntityManager.System<SharedCriminalRecordsSystem>() is { } sharedSystem)
            {
                sharedSystem.SetCriminalIcon(
                    playerName,
                    SecurityStatus.Wanted,
                    playerUid
                );
            }
        }
        catch (Exception)
        {
            // Игнорируем ошибки
        }
    }

    private bool TryGetPlayerName(EntityUid playerUid, out string playerName)
    {
        playerName = string.Empty;

        if (!_idCardSystem.TryFindIdCard(playerUid, out var idCard))
            return false;

        var idCardName = idCard.Comp.FullName;
        if (string.IsNullOrWhiteSpace(idCardName))
            return false;

        playerName = idCardName;
        return true;
    }

    private bool TryGetPlayerRecordKey(string playerName, out StationRecordKey? recordKey)
    {
        recordKey = null;

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

    private string? GetPlayerJob(EntityUid playerUid)
    {
        if (_idCardSystem.TryFindIdCard(playerUid, out var idCard))
        {
            return idCard.Comp.JobTitle;
        }
        return null;
    }

    private string? GetRecordedJobTitle(StationRecordKey recordKey)
    {
        if (_stationRecords.TryGetRecord<GeneralStationRecord>(recordKey, out var generalRecord))
        {
            return generalRecord.JobTitle;
        }
        return null;
    }

    private void SendRadioWantedMessage(EntityUid playerUid, string reason, string officer, string jobTitle)
    {
        if (!TryGetPlayerName(playerUid, out var playerName))
            return;

        var consoleQuery = EntityQueryEnumerator<CriminalRecordsConsoleComponent>();
        EntityUid? sender = null;

        while (consoleQuery.MoveNext(out var uid, out _))
        {
            sender = uid;
            break;
        }

        if (sender == null)
            return;

        var message = Loc.GetString("criminal-records-console-wanted",
            ("name", playerName),
            ("officer", officer),
            ("reason", reason),
            ("job", jobTitle));

        _radio.SendRadioMessage(sender.Value, message, "Nfsd", sender.Value);
    }
}

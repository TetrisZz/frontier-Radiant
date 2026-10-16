using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Content.Server._Triad.Shipyard.Persistence;
using Content.Server.Database;
using Content.Server.EUI;
using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Shared.Administration;
using Content.Server.GameTicking;
using Content.Shared._Triad.Shipyard.Admin;
using Content.Shared.Eui;
using Robust.Server.Player;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Network;

namespace Content.Server._Triad.Shipyard.Admin;

public sealed partial class TriadTamperAdminEui : BaseEui, ITriadTamperAuditObserver
{
    [Dependency] private ITriadShipyardAuditLog _auditLog = default!;
    [Dependency] private ITriadShipyardPermitStore _permitStore = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private IEntityManager _entMan = default!;
    [Dependency] private TriadTamperAdminEuiRegistry _registry = default!;
    [Dependency] private IAdminManager _admins = default!;

    private bool _closed;
    private int _refreshVersion;

    private int _currentPage;
    private int _currentPageSize = 50;

    // Filter state, assembled into an AuditFilter inside RefreshAsync (round-time needs an async
    // StartDate lookup to anchor, so the filter can't be built synchronously in HandleMessage).
    private int _selectedRoundId;
    private TimeSpan? _roundTimeFrom;
    private TimeSpan? _roundTimeTo;
    private Guid? _filterPlayerUserId;
    private List<TriadShipyardEventType>? _filterEventTypes;
    private string? _filterShipNameContains;

    private TriadTamperAdminEuiState _state = new();

    public TriadTamperAdminEui()
    {
        IoCManager.InjectDependencies(this);
    }

    public override void Opened()
    {
        base.Opened();
        if (!_admins.HasAdminFlag(Player, AdminFlags.Admin))
        {
            return;
        }
        _admins.OnPermsChanged += OnPermsChanged;
        _registry.Register(this);
        // Default to the live round; the client's prev / next arrows walk it from here.
        _selectedRoundId = CurrentRoundId();
        _ = RefreshAsync();
    }

    public override void Closed()
    {
        _closed = true;
        _admins.OnPermsChanged -= OnPermsChanged;
        _registry.Unregister(this);
        base.Closed();
    }

    private void OnPermsChanged(AdminPermsChangedEventArgs args)
    {
        if (args.Player == Player && !_admins.HasAdminFlag(Player, AdminFlags.Admin))
            Close();
    }

    // ITriadTamperAuditObserver: the policy service pokes this from its main-thread Update tick when
    // new audit events have been committed. Only a feed sitting on the newest page of the CURRENT round
    // tails live; a panel paged back or reviewing a past round stays put (new events land in the current
    // round, so re-querying a past round would show nothing new anyway).
    public void OnAuditChanged()
    {
        if (_currentPage != 0 || _selectedRoundId != CurrentRoundId())
            return;

        _ = RefreshAsync();
    }

    private int CurrentRoundId()
    {
        return _entMan.System<GameTicker>().RoundId;
    }

    public override EuiStateBase GetNewState()
    {
        if (_closed || !_admins.HasAdminFlag(Player, AdminFlags.Admin))
            return new TriadTamperAdminEuiState();
        return _state;
    }

    public override void HandleMessage(EuiMessageBase msg)
    {
        base.HandleMessage(msg);
        if (_closed || !_admins.HasAdminFlag(Player, AdminFlags.Admin))
        {
            if (!_closed)
                Close();
            return;
        }
        switch (msg)
        {
            case TriadTamperAdminRequestAuditPageMessage req:
                _currentPage = Math.Clamp(req.Page, 0, 100_000);
                _currentPageSize = Math.Clamp(req.PageSize, 1, 500);
                // Clamp the requested round into [1, current]; the arrows send adjacent numbers and we
                // never view a future round.
                _selectedRoundId = Math.Clamp(req.RoundId, 1, Math.Max(1, CurrentRoundId()));
                _roundTimeFrom = req.RoundTimeFromSeconds is >= 0 and var f ? TimeSpan.FromSeconds(f) : null;
                _roundTimeTo = req.RoundTimeToSeconds is >= 0 and var t ? TimeSpan.FromSeconds(t) : null;
                _filterPlayerUserId = req.PlayerUserId;
                _filterEventTypes = ParseEventTypes(req.EventTypes);
                _filterShipNameContains = req.ShipNameContains is { } name ? name[..Math.Min(name.Length, 100)] : null;
                _ = RefreshAsync();
                break;

            case TriadTamperAdminGrantPermitMessage gp:
            {
                // Per-player permit: a one-session bypass letting the player onboard unsigned/foreign
                // ships under enforce. No ship hash, no clock expiry (clears on revoke or session end).
                var adminId = Player.UserId.UserId;
                var grantedAt = DateTime.UtcNow;
                // Capture the round on the main thread; recording happens after the async grant.
                var roundId = CurrentRoundId();
                _ = GrantPermitAsync(gp.Target, adminId, grantedAt, gp.Notes, roundId);
                break;
            }

            case TriadTamperAdminRevokePermitMessage rp:
            {
                var adminId = Player.UserId.UserId;
                var roundId = CurrentRoundId();
                _ = RevokePermitAsync(rp.Target, adminId, roundId);
                break;
            }
        }
    }

    // Parse the client-sent event-type names into the enum, dropping any that don't match. Never
    // throws: a stale/modified client (or a future enum value the client doesn't yet know) must not
    // crash the EUI message handler on the game thread, which Enum.Parse would do on an unknown name.
    private static List<TriadShipyardEventType>? ParseEventTypes(List<string>? raw)
    {
        if (raw == null)
            return null;
        var result = new List<TriadShipyardEventType>(raw.Count);
        foreach (var name in raw)
        {
            if (Enum.TryParse<TriadShipyardEventType>(name, out var type))
                result.Add(type);
        }
        return result;
    }

    private async Task RefreshAsync()
    {
        try
        {
            await RefreshCoreAsync();
        }
        catch (Exception exception)
        {
            Robust.Shared.Log.Logger.ErrorS("triad.tamper", $"Unable to refresh audit panel: {exception}");
        }
    }

    private async Task RefreshCoreAsync()
    {
        if (_closed || !_admins.HasAdminFlag(Player, AdminFlags.Admin))
            return;
        var version = ++_refreshVersion;
        var currentRound = CurrentRoundId();
        if (_selectedRoundId <= 0)
            _selectedRoundId = currentRound;
        _selectedRoundId = Math.Clamp(_selectedRoundId, 1, Math.Max(1, currentRound));
        var selectedRound = _selectedRoundId;
        var pageIndex = _currentPage;
        var pageSize = _currentPageSize;
        var playerFilter = _filterPlayerUserId;
        var eventFilter = _filterEventTypes;
        var shipFilter = _filterShipNameContains;
        var timeFrom = _roundTimeFrom;
        var timeTo = _roundTimeTo;

        // Round-time boxes are offsets into the round; anchor them against the round's StartDate to get
        // the absolute timestamp window the query filters on. Only pay for the StartDate lookup when a
        // bound is actually set.
        DateTime? from = null, to = null;
        if (timeFrom.HasValue || timeTo.HasValue)
        {
            var start = await _auditLog.GetRoundStartDateAsync(selectedRound, default);
            (from, to) = TriadTamperRoundTime.AnchorRoundTime(start, timeFrom, timeTo);
        }

        var filter = new AuditFilter(
            from,
            to,
            playerFilter,
            eventFilter,
            shipFilter,
            selectedRound);

        // The three reads are independent; run them concurrently instead of awaiting in series (mirrors
        // AdminLogsEui). Each RunTriadDbCommand gets its own DbContext under the db manager's concurrency
        // semaphore, so concurrent issue is safe on both providers.
        var pageTask = _auditLog.QueryAsync(filter, pageIndex, pageSize, default);
        var roundPlayersTask = _auditLog.GetRoundPlayersAsync(selectedRound, default);
        var permitsTask = _permitStore.QueryActiveAsync(null, default);
        await Task.WhenAll(pageTask, roundPlayersTask, permitsTask);
        // Already completed by WhenAll; await unwraps synchronously (avoids the RA0004 .Result rule).
        var page = await pageTask;
        var roundPlayers = await roundPlayersTask;
        var permits = await permitsTask;

        // Resolve every display name we need in one deduped, parallel pass instead of a sequential await
        // per row/permit (the old N+1). Online sessions resolve with no DB hit; the rest are fetched
        // concurrently, and an id appearing in many rows is looked up once.
        var nameIds = new HashSet<Guid>();
        foreach (var p in permits)
        {
            nameIds.Add(p.PlayerUserId);
            nameIds.Add(p.GrantedByAdminId);
        }
        foreach (var r in page.Rows)
        {
            if (r.AdminUserId.HasValue)
                nameIds.Add(r.AdminUserId.Value);
        }

        var names = await ResolveNamesAsync(nameIds);

        var permitDtos = new List<PermitDto>(permits.Count);
        foreach (var p in permits)
        {
            permitDtos.Add(new PermitDto(
                p.PlayerUserId,
                names.GetValueOrDefault(p.PlayerUserId),
                p.GrantedByAdminId,
                names.GetValueOrDefault(p.GrantedByAdminId),
                p.GrantedAt,
                p.Notes));
        }

        // Audit rows: the acting admin's name only matters on admin-action rows (the rest have no
        // AdminUserId), and it was already resolved in the batch above.
        var auditRows = new List<AuditRowDto>(page.Rows.Count);
        foreach (var r in page.Rows)
        {
            var fingerprint = r.PublicKey != null
                ? HexFingerprint.Format(SHA256.HashData(r.PublicKey))
                : null;
            var adminName = r.AdminUserId.HasValue ? names.GetValueOrDefault(r.AdminUserId.Value) : null;
            auditRows.Add(new AuditRowDto(
                r.Id,
                r.At,
                r.EventType.ToString(),
                r.PlayerUserId,
                r.PlayerName,
                r.ShipName,
                HexFingerprint.Format(r.ShipHash),
                fingerprint,
                r.SaveTimeAppraisal,
                r.LoadTimeAppraisal,
                r.VesselId,
                r.MapId,
                adminName));
        }

        if (_closed || version != _refreshVersion || !_admins.HasAdminFlag(Player, AdminFlags.Admin))
            return;
        _state = new TriadTamperAdminEuiState
        {
            AuditPage = auditRows,
            AuditTotalCount = page.TotalCount,
            AuditPage_Index = page.Page,
            AuditPageSize = page.PageSize,

            SelectedRoundId = selectedRound,
            CurrentRoundId = currentRound,
            RoundPlayers = roundPlayers.Select(p => new PlayerOptionDto(p.UserId, p.Name)).ToList(),

            Permits = permitDtos,
        };

        StateDirty();
    }

    // Grant the permit, then log a PermitGranted row (routed through the policy service so it batches
    // and live-updates), then refresh. The round was captured on the caller's main thread.
    private async Task GrantPermitAsync(Guid target, Guid adminId, DateTime grantedAt, string? notes, int roundId)
    {
        if (!_players.TryGetSessionById(new NetUserId(target), out var session)
            || session.Status == Robust.Shared.Enums.SessionStatus.Disconnected)
            return;
        try
        {
            notes = notes is { } value ? value[..Math.Min(value.Length, 500)] : null;
            await _permitStore.GrantAsync(target, adminId, grantedAt, notes, default);
            if (_closed || !_admins.HasAdminFlag(Player, AdminFlags.Admin)
                || session.Status == Robust.Shared.Enums.SessionStatus.Disconnected)
            {
                await _permitStore.RevokeAsync(target, default);
                return;
            }
            var targetName = await ResolveNameAsync(target);
            _entMan.System<TriadTamperPolicyService>().RecordPermitAction(
                TriadShipyardEventType.PermitGranted, target, targetName, adminId, roundId > 0 ? roundId : null);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            Robust.Shared.Log.Logger.ErrorS("triad.tamper", $"Unable to grant migration permit: {exception}");
        }
    }

    private async Task RevokePermitAsync(Guid target, Guid adminId, int roundId)
    {
        try
        {
            await _permitStore.RevokeAsync(target, default);
            var targetName = await ResolveNameAsync(target);
            _entMan.System<TriadTamperPolicyService>().RecordPermitAction(
                TriadShipyardEventType.PermitRevoked, target, targetName, adminId, roundId > 0 ? roundId : null);
            await RefreshAsync();
        }
        catch (Exception exception)
        {
            Robust.Shared.Log.Logger.ErrorS("triad.tamper", $"Unable to revoke migration permit: {exception}");
        }
    }

    // Online session name first (no DB hit), else the last-seen name from the player record so an
    // offline player or admin still shows a name rather than a bare guid. Null only if neither resolves.
    private async Task<string?> ResolveNameAsync(Guid userId)
    {
        var netId = new NetUserId(userId);
        if (_players.TryGetSessionById(netId, out var session))
            return session.Name;

        var record = await _db.GetPlayerRecordByUserId(netId, default);
        return record?.LastSeenUserName;
    }

    // Batch form of ResolveNameAsync: resolve display names for a set of user ids in one pass. Online
    // sessions resolve with no DB hit; the remaining distinct ids are fetched concurrently (each its own
    // db command). Returns an entry (name or null) for every id passed in.
    private async Task<Dictionary<Guid, string?>> ResolveNamesAsync(IReadOnlyCollection<Guid> userIds)
    {
        var result = new Dictionary<Guid, string?>(userIds.Count);
        var offline = new List<Guid>();
        foreach (var id in userIds)
        {
            if (_players.TryGetSessionById(new NetUserId(id), out var session))
                result[id] = session.Name;
            else
                offline.Add(id);
        }

        if (offline.Count > 0)
        {
            var records = await Task.WhenAll(offline.Select(id =>
                _db.GetPlayerRecordByUserId(new NetUserId(id), default)));
            for (var i = 0; i < offline.Count; i++)
                result[offline[i]] = records[i]?.LastSeenUserName;
        }

        return result;
    }
}

internal static class HexFingerprint
{
    public static string Format(byte[] bytes)
    {
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}

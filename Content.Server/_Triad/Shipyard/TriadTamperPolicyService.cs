using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Content.Server._Triad.Shipyard.Persistence;
using Content.Server.Database;
using Content.Shared._Triad.CCVar;
using Robust.Shared.Configuration;
using Robust.Shared.GameObjects;
using Robust.Shared.IoC;
using Robust.Shared.Log;
using Robust.Shared.Network;
using Robust.Server.Player;
using Robust.Shared.Enums;
using Robust.Shared.Player;

namespace Content.Server._Triad.Shipyard;

public readonly record struct LoadDecision(
    bool Allow,
    TriadShipyardEventType ResolvedEvent,
    string? PopupReasonLocId);

public sealed partial class TriadTamperPolicyService : EntitySystem
{
    [Dependency] private IConfigurationManager _cfg = default!;
    [Dependency] private ShipSaveSigningSystem _signing = default!;
    [Dependency] private ITriadShipyardAuditLog _auditLog = default!;
    [Dependency] private ITriadShipyardPermitStore _permitStore = default!;
    [Dependency] private IPlayerManager _playerManager = default!;
    [Dependency] private Admin.TriadTamperAdminEuiRegistry _euiRegistry = default!;

    private ISawmill _sawmill = default!;

    // F8 fix: audit events go through a bounded channel + background consumer instead of
    // fire-and-forget _auditLog.RecordAsync calls that previously dropped silently on DB error.
    private const int AuditChannelCapacity = 10_000;
    private const int AuditBatchSize = 64;
    private Channel<TriadShipyardAuditEvent>? _auditChannel;
    private CancellationTokenSource? _auditCts;
    private Task? _auditConsumerTask;

    // Live-feed signal: the background audit consumer flips this after committing a batch; the
    // main-thread Update tick reads and clears it, then pokes open admin panels. Marshaling through
    // Update keeps the EUI StateDirty/QueueStateUpdate calls on the main thread instead of the
    // consumer's thread-pool thread. A set that races just past an Update read simply lands next tick.
    private int _auditCommittedSinceTick;

    public override void Initialize()
    {
        base.Initialize();
        _sawmill = Logger.GetSawmill("triad.tamper");

        _auditChannel = Channel.CreateBounded<TriadShipyardAuditEvent>(new BoundedChannelOptions(AuditChannelCapacity)
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false,
        });
        _auditCts = new CancellationTokenSource();
        var auditToken = _auditCts.Token;
        _auditConsumerTask = Task.Run(() => ConsumeAuditAsync(auditToken));

        // The existing signing system keeps the server key; only permits need bootstrapping.
        _ = BootstrapPermitsAsync();

        // Per-player permits are a one-session legacy-onboarding bypass: clear a player's permit on
        // disconnect (admin revoke is the other end condition). This re-adds the disconnect-clear the
        // F15 change removed, which is correct now that permits are per-player, not per-(player, hash).
        _playerManager.PlayerStatusChanged += OnPlayerStatusChanged;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _playerManager.PlayerStatusChanged -= OnPlayerStatusChanged;

        // Block the tick thread up to 5s to let the audit consumer drain committed-but-unwritten events
        // before exit. This is shutdown-only (nothing else is ticking), so the synchronous wait is
        // intentional: losing queued audit rows on a clean shutdown is worse than a brief stall.
        // Review #9 (WONTFIX).
        _auditChannel?.Writer.TryComplete();
        if (_auditConsumerTask != null)
        {
            if (!_auditConsumerTask.Wait(TimeSpan.FromSeconds(5)))
            {
                _sawmill.Warning("Audit consumer did not drain within 5s; cancelling.");
                _auditCts?.Cancel();
            }
        }
        _auditCts?.Dispose();
        _auditCts = null;
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        // Drain the commit signal on the main thread and tail the live feed for every open panel.
        // Coalesced: many committed batches between ticks collapse into a single fan-out.
        if (Interlocked.Exchange(ref _auditCommittedSinceTick, 0) == 0)
            return;

        _euiRegistry.NotifyAuditChanged();
    }

    private void OnPlayerStatusChanged(object? sender, SessionStatusEventArgs e)
    {
        // Clear a player's permit when their session ends; admin revoke is the other end condition.
        if (e.Session != null && e.NewStatus == SessionStatus.Disconnected)
            _ = RevokeSessionPermitAsync(e.Session.UserId.UserId);
    }

    private async Task RevokeSessionPermitAsync(Guid userId)
    {
        try
        {
            await _permitStore.RevokeAsync(userId, default);
            _euiRegistry.NotifyAuditChanged();
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Unable to revoke session migration permit: {exception}");
        }
    }

    private async Task BootstrapPermitsAsync()
    {
        try
        {
            await _permitStore.PopulateAsync(default);
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Failed to initialize migration permits: {ex}");
        }
    }

    private async Task ConsumeAuditAsync(CancellationToken ct)
    {
        if (_auditChannel == null)
            return;

        var reader = _auditChannel.Reader;
        var batch = new List<TriadShipyardAuditEvent>(AuditBatchSize);

        try
        {
            while (await reader.WaitToReadAsync(ct).ConfigureAwait(false))
            {
                batch.Clear();
                while (batch.Count < AuditBatchSize && reader.TryRead(out var ev))
                    batch.Add(ev);
                if (batch.Count == 0)
                    continue;
                await WriteBatchWithRetryAsync(batch, ct).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // Graceful shutdown.
        }
        catch (Exception ex)
        {
            _sawmill.Error($"Audit consumer loop crashed: {ex}");
        }
    }

    private async Task WriteBatchWithRetryAsync(IReadOnlyList<TriadShipyardAuditEvent> batch, CancellationToken ct)
    {
        const int maxAttempts = 4;
        for (var attempt = 1; attempt <= maxAttempts; attempt++)
        {
            try
            {
                await _auditLog.RecordBatchAsync(batch, ct).ConfigureAwait(false);
                // New rows are now durable; flag the main-thread Update tick to refresh open panels.
                Interlocked.Exchange(ref _auditCommittedSinceTick, 1);
                return;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                if (attempt == maxAttempts)
                {
                    _sawmill.Error(
                        $"Audit batch insert failed after {attempt} attempts; dropping {batch.Count} events. {ex}");
                    return;
                }
                var delayMs = 100 * (1 << attempt);
                try
                {
                    await Task.Delay(TimeSpan.FromMilliseconds(delayMs), ct).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
            }
        }
    }

    private void Enqueue(TriadShipyardAuditEvent ev)
    {
        var channel = _auditChannel;
        if (channel == null)
        {
            _sawmill.Warning(
                $"Audit channel not initialized; dropping event (EventType={ev.EventType}, Player={ev.PlayerUserId}).");
            return;
        }

        if (!channel.Writer.TryWrite(ev))
        {
            _sawmill.Warning(
                $"Audit channel full ({AuditChannelCapacity}); dropping event (EventType={ev.EventType}, Player={ev.PlayerUserId}).");
        }
    }

    private TamperMode ResolveMode()
    {
        var raw = _cfg.GetCVar(TriadCCVars.TamperMode);
        return raw switch
        {
            "off" => TamperMode.Off,
            "notify" => TamperMode.Notify,
            _ => TamperMode.Enforce,
        };
    }

    public AuthenticatedShipFile SignSave(string yaml, int appraisal)
    {
        return _signing.Sign(yaml, appraisal);
    }

    public AuthenticatedShipFile ReSignForMigration(AuthenticatedShipFile original, int loadTimeAppraisal)
    {
        return _signing.Sign(original.ShipYamlString(), loadTimeAppraisal);
    }

    public LoadDecision EvaluateLoad(AuthenticatedShipFile envelope, NetUserId player, string? shipName)
    {
        var mode = ResolveMode();

        if (mode == TamperMode.Off)
            return new LoadDecision(true, TriadShipyardEventType.LoadVerifiedTrusted, null);

        var hasSignature = envelope.GetInstancePublicKeyInfo() != null;
        var signatureValid = hasSignature && envelope.IsShipSigned();
        // Authority remains the existing server key, not a client-supplied key or trust flag.
        // "Valid but foreign" = a player forged their own keypair to re-sign a ship; it is rejected
        // under enforce and only ever flagged (never trusted) under notify.
        var ours = signatureValid && _signing.IsTrusted(envelope);

        // Ships signed by our own key are always allowed and never migrated.
        if (ours)
            return new LoadDecision(true, TriadShipyardEventType.LoadVerifiedTrusted, null);

        // In notify mode every state is allowed and logged with the descriptive event. A valid
        // signature from a key that isn't ours is the forged-key flare: keep LoadVerifiedUntrusted
        // as its label here (notify only).
        if (mode == TamperMode.Notify)
        {
            var ev = !hasSignature ? TriadShipyardEventType.LoadUnsigned
                : !signatureValid ? TriadShipyardEventType.LoadInvalidSignature
                : TriadShipyardEventType.LoadVerifiedUntrusted;
            return new LoadDecision(true, ev, null);
        }

        // Enforce. Permit is the per-player legacy-onboarding bypass: a permitted player may load a
        // non-our-key ship, which the load path re-signs with our key. HasPermitFor reads the cache.
        if (_permitStore.HasPermitFor(player.UserId))
            return new LoadDecision(true, TriadShipyardEventType.LoadMigrated, null);

        // Reject reasons get distinct event types so the admin feed can name why the load was blocked.
        if (!hasSignature)
            return new LoadDecision(false, TriadShipyardEventType.LoadRejectedUnsigned, "shipyard-tamper-blocked-unsigned");
        if (!signatureValid)
            return new LoadDecision(false, TriadShipyardEventType.LoadRejectedInvalidSignature, "shipyard-tamper-blocked-invalid-signature");

        // Signature valid but not our key (forged keypair). Always rejected under enforce.
        return new LoadDecision(false, TriadShipyardEventType.LoadRejectedForeignKey, "shipyard-tamper-blocked-untrusted-key");
    }

    public Task RecordSaveAsync(
        AuthenticatedShipFile envelope,
        NetUserId player,
        string? playerName,
        string? shipName,
        int appraisal,
        int? signingKeyId,
        int? roundId,
        string? serverName,
        string? vesselId,
        string? mapId,
        string? deedHolderEntity)
    {
        if (ResolveMode() == TamperMode.Off)
            return Task.CompletedTask;

        Enqueue(new TriadShipyardAuditEvent
        {
            At = DateTime.UtcNow,
            EventType = TriadShipyardEventType.SaveSigned,
            PlayerUserId = player.UserId,
            PlayerName = playerName,
            ShipName = shipName,
            ShipHash = envelope.GetHash(),
            PublicKey = envelope.GetInstancePublicKeyInfo(),
            SigningKeyId = signingKeyId,
            SaveTimeAppraisal = appraisal,
            RoundId = roundId,
            ServerName = serverName,
            VesselId = vesselId,
            MapId = mapId,
            DeedHolderEntity = deedHolderEntity,
        });
        return Task.CompletedTask;
    }

    public void RecordSave(AuthenticatedShipFile file, ICommonSession player, string shipName)
    {
        var roundId = EntityManager.System<Content.Server.GameTicking.GameTicker>().RoundId;
        _ = RecordSaveAsync(file, player.UserId, player.Name, shipName, file.Appraisal ?? 0,
            null, roundId > 0 ? roundId : null, null, null, null, null);
    }

    public void RecordLoad(AuthenticatedShipFile file, ICommonSession player, string? shipName,
        TriadShipyardEventType eventType, int? appraisal, string? sourcePath)
    {
        var roundId = EntityManager.System<Content.Server.GameTicking.GameTicker>().RoundId;
        _ = RecordLoadAsync(file, player.UserId, player.Name, shipName, eventType, appraisal,
            roundId > 0 ? roundId : null, null, null, null, sourcePath, null);
    }

    public Task RecordLoadAsync(
        AuthenticatedShipFile envelope,
        NetUserId player,
        string? playerName,
        string? shipName,
        TriadShipyardEventType eventType,
        int? loadTimeAppraisal,
        int? roundId,
        string? serverName,
        string? vesselId,
        string? mapId,
        string? sourceFilePath,
        string? deedHolderEntity)
    {
        if (ResolveMode() == TamperMode.Off)
            return Task.CompletedTask;

        Enqueue(new TriadShipyardAuditEvent
        {
            At = DateTime.UtcNow,
            EventType = eventType,
            PlayerUserId = player.UserId,
            PlayerName = playerName,
            ShipName = shipName,
            ShipHash = envelope.GetHash(),
            PublicKey = envelope.GetInstancePublicKeyInfo(),
            SaveTimeAppraisal = envelope.Appraisal,
            LoadTimeAppraisal = loadTimeAppraisal,
            RoundId = roundId,
            ServerName = serverName,
            VesselId = vesselId,
            MapId = mapId,
            SourceFilePath = sourceFilePath,
            DeedHolderEntity = deedHolderEntity,
        });
        return Task.CompletedTask;
    }

    public Task RecordRejectedLoadAsync(
        NetUserId player,
        string? playerName,
        string? sourceFilePath,
        int? roundId)
    {
        if (ResolveMode() == TamperMode.Off)
            return Task.CompletedTask;

        Enqueue(new TriadShipyardAuditEvent
        {
            At = DateTime.UtcNow,
            EventType = TriadShipyardEventType.LoadRejected,
            PlayerUserId = player.UserId,
            PlayerName = playerName,
            // No ship hash on a rejected load (often there's no valid payload at all). The column is
            // non-null, so carry an empty hash like RecordPermitAction does.
            ShipHash = Array.Empty<byte>(),
            SourceFilePath = sourceFilePath,
            RoundId = roundId,
        });
        return Task.CompletedTask;
    }

    /// <summary>
    /// Record an admin permit action (grant/revoke) into the audit feed. Routed through the same
    /// channel as load/save events so it batches and triggers the live-update fan-out. The acting
    /// admin lands in AdminUserId; the target player + ship hash identify what the permit covers.
    /// </summary>
    public void RecordPermitAction(
        TriadShipyardEventType eventType,
        Guid targetPlayer,
        string? targetPlayerName,
        Guid adminUserId,
        int? roundId)
    {
        Enqueue(new TriadShipyardAuditEvent
        {
            At = DateTime.UtcNow,
            EventType = eventType,
            PlayerUserId = targetPlayer,
            PlayerName = targetPlayerName,
            // Per-player permits aren't ship-specific; the audit ShipHash column is non-null, so
            // permit-action rows carry an empty hash.
            ShipHash = Array.Empty<byte>(),
            AdminUserId = adminUserId,
            RoundId = roundId,
        });
    }
}

internal enum TamperMode
{
    Off,
    Notify,
    Enforce,
}

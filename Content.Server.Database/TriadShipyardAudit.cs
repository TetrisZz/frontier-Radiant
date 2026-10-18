using System;

namespace Content.Server.Database;

public class TriadShipyardMigrationPermit
{
    public int Id { get; set; }
    public Guid PlayerUserId { get; set; }
    public Guid GrantedByAdminId { get; set; }
    public DateTime GrantedAt { get; set; }
    public string? Notes { get; set; }
}

public enum TriadShipyardEventType
{
    SaveSigned             = 0,
    LoadVerifiedTrusted    = 1,
    LoadVerifiedUntrusted  = 2,
    LoadUnsigned           = 3,
    LoadInvalidSignature   = 4,
    LoadMigrated           = 5,
    LoadRejected           = 6,
    PermitGranted          = 7,
    PermitRevoked          = 8,
    // Triad: enforce-mode reject reasons, split out from the generic LoadRejected (6) so the
    // admin feed can name WHY a load was blocked. These are new int values on an int-backed enum
    // column, so they need no DB migration. LoadRejected (6) is kept for the empty-payload reject
    // and for back-compat with rows written before this split.
    LoadRejectedUnsigned         = 9,
    LoadRejectedInvalidSignature = 10,
    LoadRejectedForeignKey       = 11,
}

public class TriadShipyardAuditEvent
{
    public long Id { get; set; }
    /// <summary>
    /// Event timestamp. Stored as UTC. Always use <c>DateTime.UtcNow</c> at write sites; non-UTC values will throw on Postgres.
    /// </summary>
    public DateTime At { get; set; }
    public TriadShipyardEventType EventType { get; set; }

    public int? RoundId { get; set; }
    public string? ServerName { get; set; }

    public Guid PlayerUserId { get; set; }
    public string? PlayerName { get; set; }

    public string? ShipName { get; set; }
    // NOT NULL in the DB. Default to empty (not null!) so a record path that legitimately has no
    // ship hash (rejected-load and permit-action rows) can't enqueue a null that fails the INSERT
    // and rolls back the whole batched audit transaction, taking unrelated events down with it.
    public byte[] ShipHash { get; set; } = Array.Empty<byte>();
    public byte[]? PublicKey { get; set; }
    public int? SigningKeyId { get; set; }

    public int? SaveTimeAppraisal { get; set; }
    public int? LoadTimeAppraisal { get; set; }

    public string? VesselId { get; set; }
    public string? MapId { get; set; }
    public string? SourceFilePath { get; set; }
    public string? DeedHolderEntity { get; set; }

    // Set on admin-action events (PermitGranted / PermitRevoked): which admin performed the
    // grant or revoke. Null on player save/load events.
    public Guid? AdminUserId { get; set; }
}


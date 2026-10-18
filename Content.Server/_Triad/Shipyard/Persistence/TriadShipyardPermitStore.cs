using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Content.Server.Database;
using Microsoft.EntityFrameworkCore;
using Robust.Shared.IoC;

namespace Content.Server._Triad.Shipyard.Persistence;

public sealed partial class TriadShipyardPermitStore : ITriadShipyardPermitStore
{
    [Dependency] private IServerDbManager _db = default!;
    private readonly ConcurrentDictionary<Guid, byte> _cache = new();
    private readonly SemaphoreSlim _mutations = new(1, 1);
    private volatile bool _cachePopulated;

    public async Task GrantAsync(Guid playerUserId, Guid adminId, DateTime grantedAt, string? notes, CancellationToken ct)
    {
        await _mutations.WaitAsync(ct);
        try
        {
            await _db.RunTriadDbCommand(async (db, c) =>
            {
                await db.Database.ExecuteSqlInterpolatedAsync($@"
                    INSERT INTO triad_shipyard_migration_permits
                        (player_user_id, granted_by_admin_id, granted_at, notes)
                    VALUES ({playerUserId}, {adminId}, {grantedAt}, {notes})
                    ON CONFLICT (player_user_id) DO UPDATE SET
                        granted_by_admin_id = EXCLUDED.granted_by_admin_id,
                        granted_at = EXCLUDED.granted_at,
                        notes = EXCLUDED.notes", c);
            }, ct);
            _cache[playerUserId] = 0;
        }
        finally
        {
            _mutations.Release();
        }
    }

    public async Task<bool> RevokeAsync(Guid playerUserId, CancellationToken ct)
    {
        _cache.TryRemove(playerUserId, out _);
        await _mutations.WaitAsync(ct);
        try
        {
            _cache.TryRemove(playerUserId, out _);
            return await _db.RunTriadDbCommand(async (db, c) =>
            {
                var row = await db.TriadShipyardMigrationPermits.FirstOrDefaultAsync(p => p.PlayerUserId == playerUserId, c);
                if (row == null)
                    return false;
                db.TriadShipyardMigrationPermits.Remove(row);
                await db.SaveChangesAsync(c);
                return true;
            }, ct);
        }
        finally
        {
            _mutations.Release();
        }
    }

    public bool HasPermitFor(Guid playerUserId) => _cachePopulated && _cache.ContainsKey(playerUserId);

    public Task<bool> IsPermittedAsync(Guid playerUserId, CancellationToken ct)
        => Task.FromResult(HasPermitFor(playerUserId));

    public Task<IReadOnlyList<TriadShipyardMigrationPermit>> QueryActiveAsync(Guid? playerUserIdFilter, CancellationToken ct)
        => _db.RunTriadDbCommand(async (ServerDbContext db, CancellationToken c) =>
        {
            var query = db.TriadShipyardMigrationPermits.AsNoTracking();
            if (playerUserIdFilter.HasValue)
                query = query.Where(p => p.PlayerUserId == playerUserIdFilter.Value);
            return (IReadOnlyList<TriadShipyardMigrationPermit>)await query.OrderByDescending(p => p.GrantedAt).ToListAsync(c);
        }, ct);

    public async Task PopulateAsync(CancellationToken ct)
    {
        await _mutations.WaitAsync(ct);
        try
        {
            // Permits belong to a live session. A server crash must not carry them into new sessions.
            await _db.RunTriadDbCommand(async (db, c) =>
            {
                db.TriadShipyardMigrationPermits.RemoveRange(await db.TriadShipyardMigrationPermits.ToListAsync(c));
                await db.SaveChangesAsync(c);
            }, ct);
            _cache.Clear();
            _cachePopulated = true;
        }
        finally
        {
            _mutations.Release();
        }
    }
}

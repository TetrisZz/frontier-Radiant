using System.Linq;
using System.Security.Cryptography;
using System.Threading.Tasks;
using Content.Server.Database;
using Content.Server.Discord.DiscordLink;
using Content.Shared._radiant.Supporters;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;
using Robust.Shared.Enums;
using Robust.Shared.Network;
using Robust.Shared.Player;

namespace Content.Server._radiant.Supporters;

/// <summary>Boosty's Discord role controls future loadout selections. Issued items remain for the round.</summary>
public sealed partial class SupporterSystem : SharedSupporterSystem
{
    [Dependency] private IServerDbManager _db = default!;
    [Dependency] private DiscordLink _discord = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private ITaskManager _tasks = default!;

    private sealed class Status(ICommonSession session)
    {
        public readonly ICommonSession Session = session;
        public string? DiscordId;
        public bool SentActive;
    }

    private sealed record LinkCode(NetUserId UserId, DateTime Expires);
    private readonly Dictionary<NetUserId, Status> _status = new();
    private readonly Dictionary<string, LinkCode> _codes = new();
    private DateTime _nextSweep;
    private readonly Dictionary<NetUserId, (string DiscordId, string RoleId)> _links = new();
    private Task _work = Task.CompletedTask;
    private bool _loaded;
    private bool _stopped;
    private bool _retry;
    private DateTime _nextRetry;

    public override void Initialize()
    {
        base.Initialize();
        _players.PlayerStatusChanged += OnStatusChanged;
        _discord.OnCommandReceived += OnDiscordCommand;
        _discord.OnGuildRolesChanged += OnRolesChanged;
        _discord.OnGatewayReady += OnGatewayReady;
        Subs.CVar(_config, SupporterCVars.Enabled, _ => ResetChecks());
        Subs.CVar(_config, SupporterCVars.DiscordRole, _ => ResetChecks());
        ResetChecks();
    }

    public override void Shutdown()
    {
        _stopped = true;
        _players.PlayerStatusChanged -= OnStatusChanged;
        _discord.OnCommandReceived -= OnDiscordCommand;
        _discord.OnGuildRolesChanged -= OnRolesChanged;
        _discord.OnGatewayReady -= OnGatewayReady;
        base.Shutdown();
    }

    public override bool HasAccess(NetUserId userId)
        => _config.GetCVar(SupporterCVars.Enabled) && _links.TryGetValue(userId, out var link)
            && link.RoleId.Length != 0 && link.RoleId == _config.GetCVar(SupporterCVars.DiscordRole);

    private bool Configured => _config.GetCVar(SupporterCVars.Enabled) && _discord.IsConnected &&
        ulong.TryParse(_config.GetCVar(SupporterCVars.DiscordRole), out var role) && role != 0;

    private void ResetChecks()
    {
        foreach (var status in _status.Values)
        {
            Publish(status);
        }
        Reconcile();
    }

    private void OnStatusChanged(object? sender, SessionStatusEventArgs args)
    {
        if (args.NewStatus == SessionStatus.Disconnected)
        {
            _status.Remove(args.Session.UserId);
            RemoveCodes(args.Session.UserId);
        }
        else if (args.NewStatus == SessionStatus.Connected)
        {
            var status = new Status(args.Session);
            _status[args.Session.UserId] = status;
            RaiseNetworkEvent(new SupporterStatusEvent(false), args.Session.Channel);
            if (_links.TryGetValue(args.Session.UserId, out var link)) status.DiscordId = link.DiscordId;
            Publish(status);
        }
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var now = DateTime.UtcNow;
        if (now < _nextSweep) return;
        _nextSweep = now.AddSeconds(5);
        foreach (var (code, data) in _codes.ToArray())
            if (data.Expires <= now) _codes.Remove(code);
        if (_retry && now >= _nextRetry)
        {
            _retry = false;
            Reconcile();
        }
    }

    // Serialize initial checks, gateway events and unlinking so stale REST results cannot
    // overwrite a subsequent role-removal event. All callers enqueue on the game thread.
    private Task Enqueue(Func<Task> action)
    {
        var operation = Run(_work, action);
        _work = ContinueAfterFailure(operation);
        return operation;
    }

    private static async Task ContinueAfterFailure(Task operation)
    {
        // The caller can report a failure, but the next queued gateway event must still run.
        try { await operation; }
        catch (Exception) { }
    }

    private Task CommitOnMainThread(Action action)
    {
        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _tasks.RunOnMainThread(() =>
        {
            try { action(); completion.SetResult(); }
            catch (Exception error) { completion.SetException(error); }
        });
        return completion.Task;
    }

    private async Task Run(Task previous, Func<Task> action)
    {
        await previous;
        if (_stopped) return;
        try { await action(); }
        catch (Exception error)
        {
            Log.Warning($"Supporter synchronization failed: {error.GetType().Name}");
            _tasks.RunOnMainThread(() => { _retry = true; _nextRetry = DateTime.UtcNow.AddMinutes(1); });
            throw;
        }
    }

    private void OnGatewayReady() => _tasks.RunOnMainThread(Reconcile);

    private void Reconcile()
    {
        var configured = Configured;
        var role = _config.GetCVar(SupporterCVars.DiscordRole);
        _ = Enqueue(async () =>
        {
            var links = await _db.GetSupporterLinksAsync();
            await CommitOnMainThread(() =>
            {
                if (_stopped) return;
                _links.Clear();
                foreach (var (id, link) in links) _links[id] = link;
                _loaded = true;
                foreach (var (id, status) in _status)
                {
                    status.DiscordId = links.TryGetValue(id, out var link) ? link.DiscordId : null;
                    Publish(status);
                }
            });
            if (!configured) return;
            foreach (var link in links.Values)
            {
                var active = await _discord.HasGuildRoleAsync(ulong.Parse(link.DiscordId), ulong.Parse(role));
                if (active == null) throw new InvalidOperationException("Discord unavailable");
                await SaveRole(link.DiscordId, active.Value ? role : "");
            }
        });
    }

    private void OnRolesChanged(ulong discordId, ulong[] roles)
        => _tasks.RunOnMainThread(() => ApplyDiscordRoles(discordId, roles));

    /// <summary>Gateway role changes, guild join and guild leave (empty roles).</summary>
    public Task ApplyDiscordRoles(ulong discordId, IReadOnlyList<ulong> roles)
    {
        if (!_config.GetCVar(SupporterCVars.Enabled) ||
            !ulong.TryParse(_config.GetCVar(SupporterCVars.DiscordRole), out var role) || role == 0)
            return Task.CompletedTask;
        var activeRole = roles.Contains(role) ? role.ToString() : "";
        return Enqueue(() => SaveRole(discordId.ToString(), activeRole));
    }

    private async Task SaveRole(string discordId, string roleId)
    {
        var userId = await _db.SetSupporterRoleAsync(discordId, roleId);
        if (userId == null) return;
        await CommitOnMainThread(() =>
        {
            if (_stopped) return;
            var changed = !_links.TryGetValue(userId.Value, out var old) || old.RoleId != roleId;
            _links[userId.Value] = (discordId, roleId);
            if (_status.TryGetValue(userId.Value, out var status))
            {
                status.DiscordId = discordId;
                Publish(status);
            }
            if (changed) Log.Info($"Persisted supporter role for {userId}: {(roleId.Length == 0 ? "none" : roleId)}");
        });
    }

    private void Publish(Status status)
    {
        var active = HasAccess(status.Session.UserId);
        if (active == status.SentActive) return;
        status.SentActive = active;
        RaiseNetworkEvent(new SupporterStatusEvent(active), status.Session.Channel);
        Log.Info($"Supporter access for {status.Session.UserId}: {active}");
    }

    public string CreateLinkCode(ICommonSession session)
    {
        if (!Configured) return Loc.GetString("supporter-not-configured");
        RemoveCodes(session.UserId);
        var code = Convert.ToHexString(RandomNumberGenerator.GetBytes(16));
        _codes.Add(code, new LinkCode(session.UserId, DateTime.UtcNow.AddMinutes(10)));
        var command = _discord.BotPrefix + "link " + code;
        RaiseNetworkEvent(new SupporterLinkCodeEvent(command), session.Channel);
        return Loc.GetString("supporter-link-code", ("command", command));
    }

    public string GetStatus(ICommonSession session)
    {
        if (!_config.GetCVar(SupporterCVars.Enabled)) return Loc.GetString("supporter-not-configured");
        if (!_loaded || !_status.TryGetValue(session.UserId, out var status))
            return Loc.GetString("supporter-checking");
        if (status.DiscordId == null) return Loc.GetString("supporter-not-linked");
        return Loc.GetString(HasAccess(session.UserId) ? "supporter-active" : "supporter-inactive");
    }

    private void RemoveCodes(NetUserId userId)
    {
        foreach (var (code, data) in _codes.ToArray())
            if (data.UserId == userId) _codes.Remove(code);
    }

    private void OnDiscordCommand(CommandReceivedEventArgs args)
    {
        if (args.Command != "link" || args.Message.Author.IsBot || args.Message.GuildId != null) return;
        var code = args.Arguments.Trim().ToUpperInvariant();
        if (code.Length != 32) return;
        _tasks.RunOnMainThread(() => CompleteLink(args.Message.Author.Id, args.Message.ChannelId, code));
    }

    private async void CompleteLink(ulong discordId, ulong channelId, string code)
    {
        if (!Configured || !_codes.Remove(code, out var data) || data.Expires <= DateTime.UtcNow)
        {
            await Reply(channelId, "supporter-invalid-code");
            return;
        }
        var role = _config.GetCVar(SupporterCVars.DiscordRole);
        try
        {
            await Enqueue(async () =>
            {
                if (!await _db.TryLinkDiscordAsync(data.UserId, discordId.ToString()))
                {
                    await Reply(channelId, "supporter-link-conflict");
                    return;
                }
                Log.Info($"Linked game account {data.UserId} to Discord {discordId}");
                // A newly linked account needs one initial check of an already assigned role.
                var active = await _discord.HasGuildRoleAsync(discordId, ulong.Parse(role));
                if (active == null) throw new InvalidOperationException("Discord unavailable");
                await SaveRole(discordId.ToString(), active.Value ? role : "");
                await Reply(channelId, "supporter-linked");
            });
        }
        catch (Exception)
        {
            await Reply(channelId, "supporter-link-failed");
        }
    }

    private async Task Reply(ulong channelId, string key)
    {
        try { await _discord.ReplyAsync(channelId, Loc.GetString(key)); }
        catch (Exception error) { Log.Warning($"Could not reply to link command: {error.GetType().Name}"); }
    }

    public Task Unlink(NetUserId userId)
    {
        RemoveCodes(userId);
        return Enqueue(async () =>
        {
            await _db.RemoveDiscordLinkAsync(userId);
            await CommitOnMainThread(() =>
            {
                _links.Remove(userId);
                if (_status.TryGetValue(userId, out var status))
                {
                    status.DiscordId = null;
                    Publish(status);
                }
                Log.Info($"Unlinked supporter account {userId}");
            });
        });
    }
}

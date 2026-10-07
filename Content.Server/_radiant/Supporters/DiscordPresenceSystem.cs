using Content.Server.Discord.DiscordLink;
using Content.Shared._radiant.Supporters;
using Robust.Server.Player;
using Robust.Shared.Asynchronous;
using Robust.Shared.Configuration;

namespace Content.Server._radiant.Supporters;

/// <summary>Publishes connected players (including lobby and ghosts) in the bot's activity.</summary>
public sealed partial class DiscordPresenceSystem : EntitySystem
{
    [Dependency] private DiscordLink _discord = default!;
    [Dependency] private IPlayerManager _players = default!;
    [Dependency] private IConfigurationManager _config = default!;
    [Dependency] private ITaskManager _tasks = default!;
    private bool _ready;
    private bool _sending;
    private bool _force;
    private int? _lastPlayers;
    private DateTime _nextUpdate;

    public override void Initialize()
    {
        base.Initialize();
        _discord.OnGatewayReady += OnReady;
        Subs.CVar(_config, SupporterCVars.ShowOnline, _ => _force = true);
    }

    public override void Shutdown()
    {
        _discord.OnGatewayReady -= OnReady;
        _ready = false;
        base.Shutdown();
    }

    private void OnReady() => _tasks.RunOnMainThread(() =>
    {
        _ready = true;
        _force = true;
    });

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        if (!_ready || _sending || DateTime.UtcNow < _nextUpdate) return;
        int? players = _config.GetCVar(SupporterCVars.ShowOnline) ? _players.PlayerCount : null;
        if (!_force && players == _lastPlayers) return;
        _force = false;
        _sending = true;
        _nextUpdate = DateTime.UtcNow.AddSeconds(15);
        Send(players);
    }

    private async void Send(int? players)
    {
        try
        {
            await _discord.UpdatePlayerPresenceAsync(players);
            _tasks.RunOnMainThread(() => { _lastPlayers = players; _sending = false; });
        }
        catch (Exception error)
        {
            Log.Warning($"Could not update Discord online status: {error.GetType().Name}");
            _tasks.RunOnMainThread(() =>
            {
                _sending = false;
                _force = true;
                _nextUpdate = DateTime.UtcNow.AddMinutes(1);
            });
        }
    }
}

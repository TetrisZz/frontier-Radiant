using System.Threading.Tasks;
using Content.Shared.CCVar;
using System.Net;
using System.Linq;
using NetCord;
using NetCord.Gateway;
using NetCord.Rest;
using Robust.Shared.Configuration;

namespace Content.Server.Discord.DiscordLink;

/// <summary>
/// Represents the arguments for the <see cref="DiscordLink.OnCommandReceived"/> event.
/// </summary>
public sealed class CommandReceivedEventArgs
{
    /// <summary>
    /// The command that was received. This is the first word in the message, after the bot prefix.
    /// </summary>
    public string Command { get; init; } = string.Empty;

    /// <summary>
    /// The arguments to the command. This is everything after the command
    /// </summary>
    public string Arguments { get; init; } = string.Empty;
    /// <summary>
    /// Information about the message that the command was received from. This includes the message content, author, etc.
    /// Use this to reply to the message, delete it, etc.
    /// </summary>
    public Message Message { get; init; } = default!;
}

/// <summary>
/// Handles the connection to Discord and provides methods to interact with it.
/// </summary>
public sealed class DiscordLink : IPostInjectInit
{
    [Dependency] private readonly ILogManager _logManager = default!;
    [Dependency] private readonly IConfigurationManager _configuration = default!;

    /// <summary>
    ///    The Discord client. This is null if the bot is not connected.
    /// </summary>
    /// <remarks>
    ///     This should not be used directly outside of DiscordLink. So please do not make it public. Use the methods in this class instead.
    /// </remarks>
    private GatewayClient? _client;
    private ISawmill _sawmill = default!;
    private ISawmill _sawmillLog = default!;

    private ulong _guildId;
    private string _botToken = string.Empty;

    public string BotPrefix = default!;
    /// <summary>
    /// If the bot is currently connected to Discord.
    /// </summary>
    public bool IsConnected => _client != null;

    #region Events

    /// <summary>
    ///     Event that is raised when a command is received from Discord.
    /// </summary>
    public event Action<CommandReceivedEventArgs>? OnCommandReceived;
    /// <summary>
    ///     Event that is raised when a message is received from Discord. This is raised for every message, including commands.
    /// </summary>
    public event Action<Message>? OnMessageReceived;
    public event Action<ulong, ulong[]>? OnGuildRolesChanged;
    public event Action? OnGatewayReady;

    public void RegisterCommandCallback(Action<CommandReceivedEventArgs> callback, string command)
    {
        OnCommandReceived += args =>
        {
            if (args.Command == command)
                callback(args);
        };
    }

    #endregion

    public void Initialize()
    {
        _configuration.OnValueChanged(CCVars.DiscordGuildId, OnGuildIdChanged, true);
        _configuration.OnValueChanged(CCVars.DiscordPrefix, OnPrefixChanged, true);

        if (_configuration.GetCVar(CCVars.DiscordToken) is not { } token || token == string.Empty)
        {
            _sawmill.Info("No Discord token specified, not connecting.");
            return;
        }

        // If the Guild ID is empty OR the prefix is empty, we don't want to connect to Discord.
        if (_guildId == 0 || BotPrefix == string.Empty)
        {
            // This is a warning, not info, because it's a configuration error.
            // It is valid to not have a Discord token set which is why the above check is an info.
            // But if you have a token set, you should also have a guild ID and prefix set.
            _sawmill.Warning("No Discord guild ID or prefix specified, not connecting.");
            return;
        }

        _client = new GatewayClient(new BotToken(token), new GatewayClientConfiguration()
        {
            Intents = GatewayIntents.Guilds
                             | GatewayIntents.GuildUsers
                             | GatewayIntents.GuildMessages
                             | GatewayIntents.MessageContent
                             | GatewayIntents.DirectMessages,
            Logger = new DiscordSawmillLogger(_sawmillLog),
        });
        _client.MessageCreate += OnCommandReceivedInternal;
        _client.MessageCreate += OnMessageReceivedInternal;
        _client.GuildUserUpdate += OnGuildUserChanged;
        _client.GuildUserAdd += OnGuildUserChanged;
        // Deleting the donor role itself may not emit a member update for every member.
        _client.RoleDelete += args =>
        {
            if (args.GuildId == _guildId) OnGatewayReady?.Invoke();
            return default;
        };
        _client.GuildUserRemove += args =>
        {
            if (args.GuildId == _guildId) OnGuildRolesChanged?.Invoke(args.User.Id, []);
            return default;
        };
        _client.Resume += () =>
        {
            OnGatewayReady?.Invoke();
            return default;
        };

        _botToken = token;
        // Since you cannot change the token while the server is running / the DiscordLink is initialized,
        // we can just set the token without updating it every time the cvar changes.

        _client.Ready += _ =>
        {
            _sawmill.Info("Discord client ready.");
            OnGatewayReady?.Invoke();
            return default;
        };

        Task.Run(async () =>
        {
            try
            {
                await _client.StartAsync();
                _sawmill.Info("Connected to Discord.");
            }
            catch (Exception e)
            {
                _sawmill.Error("Failed to connect to Discord!", e);
            }
        });
    }

    public async Task Shutdown()
    {
        if (_client != null)
        {
            _sawmill.Info("Disconnecting from Discord.");

            // Unsubscribe from the events.
            _client.MessageCreate -= OnCommandReceivedInternal;
            _client.MessageCreate -= OnMessageReceivedInternal;

            await _client.CloseAsync();
            _client.Dispose();
            _client = null;
        }

        _configuration.UnsubValueChanged(CCVars.DiscordGuildId, OnGuildIdChanged);
        _configuration.UnsubValueChanged(CCVars.DiscordPrefix, OnPrefixChanged);
    }

    void IPostInjectInit.PostInject()
    {
        _sawmill = _logManager.GetSawmill("discord.link");
        _sawmillLog = _logManager.GetSawmill("discord.link.log");
    }

    private void OnGuildIdChanged(string guildId)
    {
        _guildId = ulong.TryParse(guildId, out var id) ? id : 0;
    }

    private void OnPrefixChanged(string prefix)
    {
        BotPrefix = prefix;
    }

    private ValueTask OnCommandReceivedInternal(Message message)
    {
        var content = message.Content;
        // If the message doesn't start with the bot prefix, ignore it.
        if (!content.StartsWith(BotPrefix))
            return ValueTask.CompletedTask;

        // Split the message into the command and the arguments.
        var trimmedInput = content[BotPrefix.Length..].Trim();
        var firstSpaceIndex = trimmedInput.IndexOf(' ');

        string command, arguments;

        if (firstSpaceIndex == -1)
        {
            command = trimmedInput;
            arguments = string.Empty;
        }
        else
        {
            command = trimmedInput[..firstSpaceIndex];
            arguments = trimmedInput[(firstSpaceIndex + 1)..].Trim();
        }

        // Raise the event!
        OnCommandReceived?.Invoke(new CommandReceivedEventArgs
        {
            Command = command,
            Arguments = arguments,
            Message = message,
        });
        return ValueTask.CompletedTask;
    }

    private ValueTask OnMessageReceivedInternal(Message message)
    {
        OnMessageReceived?.Invoke(message);
        return ValueTask.CompletedTask;
    }

    private ValueTask OnGuildUserChanged(GuildUser user)
    {
        if (user.GuildId == _guildId) OnGuildRolesChanged?.Invoke(user.Id, user.RoleIds.ToArray());
        return ValueTask.CompletedTask;
    }

    #region Proxy methods

    public async Task UpdatePlayerPresenceAsync(int? players)
    {
        if (_client == null) return;
        await _client.UpdatePresenceAsync(CreatePlayerPresence(players));
    }

    public static PresenceProperties CreatePlayerPresence(int? players)
        => new(UserStatusType.Online)
        {
            Activities = players is { } count
                ? new[] { new UserActivityProperties($"Онлайн сервера: {count}", UserActivityType.Watching) }
                : Array.Empty<UserActivityProperties>(),
        };

    /// <summary>Null means unavailable; false is a confirmed missing role or guild member.</summary>
    public async Task<bool?> HasGuildRoleAsync(ulong userId, ulong roleId)
    {
        if (_client == null || _guildId == 0) return null;
        try
        {
            var member = await _client.Rest.GetGuildUserAsync(_guildId, userId);
            return member.RoleIds.Contains(roleId);
        }
        catch (RestException error) when (error.StatusCode == HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task ReplyAsync(ulong channelId, string message)
    {
        if (_client == null) return;
        await _client.Rest.SendMessageAsync(channelId, new MessageProperties
        {
            Content = message,
            AllowedMentions = AllowedMentionsProperties.None,
        });
    }

    /// <summary>
    /// Sends a message to a Discord channel with the specified ID. Without any mentions.
    /// </summary>
    public async Task SendMessageAsync(ulong channelId, string message)
    {
        if (_client == null)
        {
            return;
        }

        var channel = await _client.Rest.GetChannelAsync(channelId) as TextChannel;
        if (channel == null)
        {
            _sawmill.Error("Tried to send a message to Discord but the channel {Channel} was not found.", channel);
            return;
        }

        await channel.SendMessageAsync(new MessageProperties()
        {
            AllowedMentions = AllowedMentionsProperties.None,
            Content = message,
        });
    }

    #endregion
}

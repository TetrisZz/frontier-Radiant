using System.Globalization;
using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;
using Robust.Shared.Network;

namespace Content.Server._radiant.Supporters;

[AnyCommand]
public sealed partial class SupporterLinkCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    public string Command => "supporterlink";
    public string Description => Loc.GetString("supporter-command-link");
    public string Help => Command;
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player || args.Length != 0) { shell.WriteError(Help); return; }
        shell.WriteLine(_entities.System<SupporterSystem>().CreateLinkCode(player));
    }
}

[AnyCommand]
public sealed partial class SupporterStatusCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    public string Command => "supporterstatus";
    public string Description => Loc.GetString("supporter-command-status");
    public string Help => Command;
    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player || args.Length != 0) { shell.WriteError(Help); return; }
        shell.WriteLine(_entities.System<SupporterSystem>().GetStatus(player));
    }
}

[AdminCommand(AdminFlags.Permissions)]
public sealed partial class SupporterUnlinkCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    public string Command => "supporterunlink";
    public string Description => Loc.GetString("supporter-command-unlink");
    public string Help => "supporterunlink <game account UUID>";
    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1 || !Guid.TryParse(args[0], out var id)) { shell.WriteError(Help); return; }
        try
        {
            await _entities.System<SupporterSystem>().Unlink(new NetUserId(id));
            shell.WriteLine(Loc.GetString("supporter-unlinked"));
        }
        catch (Exception) { shell.WriteError(Loc.GetString("supporter-link-failed")); }
    }
}

[AdminCommand(AdminFlags.Permissions)]
public sealed class SupporterGrantCommand : IConsoleCommand
{
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IPlayerLocator _players = default!;
    [Dependency] private readonly IAdminLogManager _logs = default!;
    [Dependency] private readonly ILogManager _logManager = default!;

    public string Command => "supportergrant";
    public string Description => Loc.GetString("supporter-command-grant");
    public string Help => Loc.GetString("supporter-command-grant-help");

    public async void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 1)
        {
            shell.WriteError(Help);
            return;
        }

        try
        {
            var target = await _players.LookupIdByNameOrIdAsync(args[0]);
            if (target == null)
            {
                shell.WriteError(Loc.GetString("supporter-grant-not-found", ("player", args[0])));
                return;
            }

            var expiry = await _entities.System<SupporterSystem>().GrantManualAccess(target.UserId);
            var formattedExpiry = expiry.ToString("u", CultureInfo.InvariantCulture);
            _logs.Add(LogType.AdminCommands, LogImpact.High,
                $"{shell.Player?.Name ?? "Server console"} ({shell.Player?.UserId}) granted supporter access to {target.Username} ({target.UserId}) until {formattedExpiry}");
            shell.WriteLine(Loc.GetString("supporter-grant-success", ("player", target.Username), ("expiry", formattedExpiry)));
        }
        catch (Exception exception)
        {
            _logManager.GetSawmill("supporters").Error($"Failed to grant supporter access: {exception}");
            shell.WriteError(Loc.GetString("supporter-grant-failed"));
        }
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args)
        => args.Length == 1
            ? CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), Loc.GetString("supporter-command-grant-target"))
            : CompletionResult.Empty;
}

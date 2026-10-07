using Content.Server.Administration;
using Content.Shared.Administration;
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

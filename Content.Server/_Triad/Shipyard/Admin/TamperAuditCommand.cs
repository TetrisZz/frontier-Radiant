using Content.Server.Administration;
using Content.Server.Administration.Managers;
using Content.Server.EUI;
using Content.Shared.Administration;
using Robust.Shared.Console;
using Robust.Shared.IoC;

namespace Content.Server._Triad.Shipyard.Admin;

[AdminCommand(AdminFlags.Admin)]
public sealed class TamperAuditCommand : IConsoleCommand
{
    public string Command => "tamperaudit";
    public string Description => Loc.GetString("tamper-audit-command-description");
    public string Help => Command;

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (shell.Player is not { } player)
        {
            shell.WriteError(Loc.GetString("shell-cannot-run-command-from-server"));
            return;
        }

        if (!IoCManager.Resolve<IAdminManager>().HasAdminFlag(player, AdminFlags.Admin))
            return;
        var eui = IoCManager.Resolve<EuiManager>();
        var ui = new TriadTamperAdminEui();
        eui.OpenEui(ui, player);
    }
}

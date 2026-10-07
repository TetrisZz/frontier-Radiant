using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Shared.Console;

namespace Content.Server._radiant.Medical.Genetics;

[AdminCommand(AdminFlags.Debug)]
public sealed partial class GeneticSequenceCommand : IConsoleCommand
{
    [Dependency] private IEntityManager _entities = default!;
    [Dependency] private IAdminLogManager _logs = default!;
    public string Command => "geneticssequence";
    public string Description => Loc.GetString("genetics-admin-sequence-description");
    public string Help => "geneticssequence <sample or console NetEntity> <gene ID> [ATGCAT]";

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length is < 2 or > 3)
        {
            shell.WriteError(Help);
            return;
        }
        var genetics = _entities.System<GeneticBodySystem>();
        if (!NetEntity.TryParse(args[0], out var net) || !_entities.TryGetEntity(net, out var target)
            || target is not { } uid || !genetics.TryGetResearchSequence(uid, args[1], out var sequence))
        {
            shell.WriteError(Loc.GetString("genetics-admin-unknown-gene"));
            return;
        }
        if (args.Length == 3)
        {
            var replacement = args[2].ToUpperInvariant();
            if (!genetics.TrySetResearchSequence(uid, args[1], replacement))
            {
                shell.WriteError(Loc.GetString("genetics-admin-sequence-failed"));
                return;
            }
            _logs.Add(LogType.AdminCommands, LogImpact.High,
                $"{shell.Player?.Name ?? "Server"} changed donor genetics sequence {args[1]} at {net} from {sequence} to {replacement}.");
            sequence = replacement;
            shell.WriteLine(Loc.GetString("genetics-admin-sequence-set"));
        }
        shell.WriteLine($"{args[1]}: {sequence}");
    }
}

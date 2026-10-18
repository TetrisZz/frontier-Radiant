using Content.Server.Administration;
using Content.Server.Administration.Logs;
using Content.Shared._radiant.Careers;
using Content.Shared.Administration;
using Content.Shared.Database;
using Robust.Server.Player;
using Robust.Shared.Console;

namespace Content.Server._radiant.Careers;

[AdminCommand(AdminFlags.Admin)]
public sealed class CareerAddTimeCommand : IConsoleCommand
{
    [Dependency] private readonly IPlayerManager _players = default!;
    [Dependency] private readonly IEntityManager _entities = default!;
    [Dependency] private readonly IAdminLogManager _logs = default!;

    public string Command => "career_addtime";
    public string Description => Loc.GetString("cmd-career-addtime-desc");
    public string Help => Loc.GetString("cmd-career-addtime-help");

    public void Execute(IConsoleShell shell, string argStr, string[] args)
    {
        if (args.Length != 2)
        {
            shell.WriteError(Help);
            return;
        }
        if (!int.TryParse(args[1], out var minutes) || minutes <= 0)
        {
            shell.WriteError(Loc.GetString("cmd-career-addtime-invalid-minutes"));
            return;
        }
        if (!_players.TryGetSessionByUsername(args[0], out var session))
        {
            shell.WriteError(Loc.GetString("parse-session-fail", ("username", args[0])));
            return;
        }

        var careers = _entities.System<CareerSystem>();
        if (session.AttachedEntity is not { } player || !careers.IsWorking(player))
        {
            shell.WriteError(Loc.GetString("cmd-career-addtime-not-working"));
            return;
        }
        if (!careers.RecordActivity(player, CareerObjectiveType.DutyTime, minutes * 60d, [], []))
        {
            shell.WriteError(Loc.GetString("cmd-career-addtime-no-objective"));
            return;
        }

        _logs.Add(LogType.AdminCommands, LogImpact.High,
            $"{shell.Player?.Name ?? "Server console"} ({shell.Player?.UserId}) added {minutes} career duty minutes to {_entities.ToPrettyString(player)} ({session.UserId})");
        shell.WriteLine(Loc.GetString("cmd-career-addtime-success", ("username", session.Name), ("minutes", minutes)));
    }

    public CompletionResult GetCompletion(IConsoleShell shell, string[] args) => args.Length switch
    {
        1 => CompletionResult.FromHintOptions(CompletionHelper.SessionNames(), Loc.GetString("cmd-career-addtime-user")),
        2 => CompletionResult.FromHint(Loc.GetString("cmd-career-addtime-minutes")),
        _ => CompletionResult.Empty,
    };
}

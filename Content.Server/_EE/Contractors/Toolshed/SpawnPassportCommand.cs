using Content.Server._EE.Contractors.Systems;
using Content.Server.Administration;
using Content.Server.GameTicking;
using Content.Shared.Administration;
using Robust.Shared.Player;
using Robust.Shared.Toolshed;
using Robust.Shared.Toolshed.Errors;

namespace Content.Server._EE.Contractors.Toolshed;

[ToolshedCommand, AdminCommand(AdminFlags.Spawn)]
public sealed class SpawnPassportCommand : ToolshedCommand
{
    private PassportSystem? _passports;
    private GameTicker? _ticker;

    [CommandImplementation]
    public IEnumerable<EntityUid> SpawnPassport([PipedArgument] IEnumerable<EntityUid> input)
    {
        _passports ??= GetSys<PassportSystem>();
        _ticker ??= GetSys<GameTicker>();

        foreach (var uid in input)
        {
            if (!TryComp(uid, out ActorComponent? actor))
                continue;

            _passports.SpawnPassportForPlayer(uid, _ticker.GetPlayerProfile(actor.PlayerSession));
            yield return uid;
        }
    }

    [CommandImplementation]
    public void SpawnPassport(IInvocationContext ctx)
    {
        if (ExecutingEntity(ctx) is not { } uid)
        {
            if (ctx.Session is { } session)
                ctx.ReportError(new SessionHasNoEntityError(session));
            else
                ctx.ReportError(new NotForServerConsoleError());
            return;
        }

        if (!TryComp(uid, out ActorComponent? actor))
            return;

        _passports ??= GetSys<PassportSystem>();
        _ticker ??= GetSys<GameTicker>();
        _passports.SpawnPassportForPlayer(uid, _ticker.GetPlayerProfile(actor.PlayerSession));
    }
}

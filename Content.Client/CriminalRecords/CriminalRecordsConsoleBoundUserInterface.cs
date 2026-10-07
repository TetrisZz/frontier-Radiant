using Content.Shared.Access.Systems;
using Content.Shared._radiant.Dossiers;
using Content.Shared.CriminalRecords;
using Content.Shared.CriminalRecords.Components;
using Content.Shared.Security;
using Content.Shared.StationRecords;
using Robust.Client.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Client.CriminalRecords;

public sealed class CriminalRecordsConsoleBoundUserInterface : BoundUserInterface
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPlayerManager _playerManager = default!;
    private readonly AccessReaderSystem _accessReader;

    private CriminalRecordsConsoleWindow? _window;

    public CriminalRecordsConsoleBoundUserInterface(EntityUid owner, Enum uiKey) : base(owner, uiKey)
    {
        _accessReader = EntMan.System<AccessReaderSystem>();
    }

    protected override void Open()
    {
        base.Open();

        var comp = EntMan.GetComponent<CriminalRecordsConsoleComponent>(Owner);

        _window = new(Owner, comp.MaxStringLength, _playerManager, _proto, _random, _accessReader);
        _window.OnKeySelected += key =>
            SendMessage(new SelectStationRecord(key));
        _window.OnFiltersChanged += (type, filterValue) =>
            SendMessage(new SetStationRecordFilter(type, filterValue));
        _window.OnStatusSelected += status =>
            SendMessage(new CriminalRecordChangeStatus(status, null));
        _window.OnDialogConfirmed += (status, reason) =>
            SendMessage(new CriminalRecordChangeStatus(status, reason));
        _window.OnStatusFilterPressed += (statusFilter) =>
            SendMessage(new CriminalRecordSetStatusFilter(statusFilter));
        _window.OnDossierRequested += () => SendMessage(new OpenSecurityDossierMessage());
        _window.OnClose += Close;
    }

    protected override void UpdateState(BoundUserInterfaceState state)
    {
        base.UpdateState(state);

        if (state is not CriminalRecordsConsoleState cast)
            return;

        _window?.UpdateState(cast);
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        _window?.Close();
    }
}

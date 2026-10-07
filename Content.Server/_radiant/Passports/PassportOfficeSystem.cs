using Content.Server._EE.Contractors.Systems;
using Content.Shared._radiant.Passports;
using Content.Shared._radiant.Dossiers;
using Content.Shared.Access.Systems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Server._radiant.Dossiers;
using Content.Shared.Humanoid.Prototypes;
using Robust.Server.GameObjects;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Passports;

public sealed partial class PassportOfficeSystem : EntitySystem
{
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private SharedInteractionSystem _interaction = default!;
    [Dependency] private PassportSystem _passports = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private IPrototypeManager _prototypes = default!;

    public override void Initialize()
    {
        base.Initialize();
        Subs.BuiEvents<PassportOfficeComponent>(PassportOfficeUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>(OnOpened);
            subs.Event<PassportOfficeRefreshMessage>(OnRefresh);
            subs.Event<PassportOfficeIssueMessage>(OnIssue);
        });
    }

    private void OnOpened(Entity<PassportOfficeComponent> ent, ref BoundUIOpenedEvent args)
        => SendState(ent.Owner, args.Actor);

    private void OnRefresh(Entity<PassportOfficeComponent> ent, ref PassportOfficeRefreshMessage args)
        => SendState(ent.Owner, args.Actor);

    private void OnIssue(Entity<PassportOfficeComponent> ent, ref PassportOfficeIssueMessage args)
    {
        if (!CanUse(ent.Owner, args.Actor) || !TryGetEntity(args.Target, out var target) ||
            target == null || !HasComp<ActorComponent>(target.Value) ||
            !HasComp<HumanoidAppearanceComponent>(target.Value) ||
            !_interaction.InRangeUnobstructed(ent.Owner, target.Value, 2.5f))
            return;

        var name = args.Name.Trim();
        if (name.Length is < 2 or > 64 || args.Age is < 1 or > 1000 || args.Height is < 1 or > 1000 ||
            args.Residence.Length > 256 || args.EmergencyContact.Length > 256 ||
            Array.IndexOf(DossierFamilyStatus.Values, args.FamilyStatus) < 0)
        {
            _popup.PopupEntity(Loc.GetString("radiant-passport-office-invalid"), ent, args.Actor);
            return;
        }

        if (_passports.IssuePassport(target.Value, Transform(ent).Coordinates, name,
                args.Age, args.Height, args.Residence.Trim(), args.EmergencyContact.Trim(), args.FamilyStatus) == null)
            return;

        _popup.PopupEntity(Loc.GetString("radiant-passport-office-issued"), ent, args.Actor);
        SendState(ent.Owner, args.Actor);
    }

    private bool CanUse(EntityUid console, EntityUid user)
        => _access.IsAllowed(user, console) && _interaction.InRangeUnobstructed(user, console);

    private void SendState(EntityUid console, EntityUid user)
    {
        if (!CanUse(console, user))
            return;

        var candidates = new Dictionary<NetEntity, PassportOfficeApplicant>();
        var query = EntityQueryEnumerator<PassportIdentityComponent, HumanoidAppearanceComponent>();
        while (query.MoveNext(out var uid, out var identity, out var appearance))
            if (HasComp<ActorComponent>(uid) && _interaction.InRangeUnobstructed(console, uid, 2.5f))
            {
                var height = _prototypes.TryIndex<SpeciesPrototype>(appearance.Species, out var species)
                    ? (int)MathF.Round(appearance.Height * species.AverageHeight) : 0;
                var residence = "";
                var emergency = "";
                var familyStatus = "";
                if (TryComp<DossierHolderComponent>(uid, out var dossier))
                {
                    residence = dossier.Record.Residence;
                    emergency = dossier.Record.EmergencyContact;
                    familyStatus = dossier.Record.FamilyStatus;
                }
                candidates[GetNetEntity(uid)] = new PassportOfficeApplicant(Name(uid),
                    species != null ? Loc.GetString(species.Name) : appearance.Species.Id,
                    Loc.GetString("ee-passport-sex-" + appearance.Sex.ToString().ToLowerInvariant()),
                    appearance.Age, height, residence, emergency, familyStatus, appearance.Sex);
            }

        _ui.SetUiState(console, PassportOfficeUiKey.Key, new PassportOfficeUiState(candidates));
    }
}

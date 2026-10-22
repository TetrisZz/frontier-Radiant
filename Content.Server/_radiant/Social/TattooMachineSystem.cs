using System.Linq;
using Content.Server._radiant.Dossiers;
using Content.Shared._radiant.Social;
using Content.Shared.Body.Part;
using Content.Shared.Body.Systems;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._radiant.Social;

public sealed class TattooMachineSystem : EntitySystem
{
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly DossierSystem _dossiers = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedBodySystem _body = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<TattooMachineComponent, AfterInteractEvent>(OnUse);
        SubscribeLocalEvent<TattooMachineComponent, TattooApplyEvent>(OnFinished);
        SubscribeLocalEvent<TextTattooComponent, ExaminedEvent>(OnExamined);
        Subs.BuiEvents<TattooMachineComponent>(TattooUiKey.Editor, s => s.Event<TattooProposeMessage>(OnPropose));
        Subs.BuiEvents<TattooMachineComponent>(TattooUiKey.Consent, s =>
        {
            s.Event<BoundUIOpenedEvent>(OnConsentOpened);
            s.Event<TattooConsentMessage>(OnConsent);
            s.Event<BoundUIClosedEvent>(OnConsentClosed);
        });
    }

    private bool CanWork(Entity<TattooMachineComponent> ent, EntityUid artist, EntityUid patient)
        => _hands.IsHolding(artist, ent.Owner) && _interaction.InRangeUnobstructed(artist, patient) &&
           _mobs.IsAlive(artist) && _mobs.IsAlive(patient) && HasComp<HumanoidAppearanceComponent>(patient) &&
           TryComp<DossierHolderComponent>(patient, out var holder) && holder.Loaded && !holder.Archived;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<TattooMachineComponent>();
        while (query.MoveNext(out var uid, out var machine))
        {
            if (machine.Pending == null ||
                _timing.CurTime <= machine.ExpiresAt && machine.Artist is { } artist &&
                machine.Patient is { } patient && CanWork((uid, machine), artist, patient))
                continue;
            machine.Pending = null;
            machine.Accepted = false;
            machine.RequestId++;
            _ui.CloseUi(uid, TattooUiKey.Consent);
            UpdateState((uid, machine));
        }
    }

    private void OnUse(Entity<TattooMachineComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } patient || !CanWork(ent, args.User, patient))
            return;
        args.Handled = true;
        _ui.CloseUi(ent.Owner, TattooUiKey.Editor);
        _ui.CloseUi(ent.Owner, TattooUiKey.Consent);
        ent.Comp.RequestId++;
        ent.Comp.Artist = args.User;
        ent.Comp.Patient = patient;
        ent.Comp.Pending = null;
        ent.Comp.Accepted = false;
        UpdateState(ent);
        _ui.TryOpenUi(ent.Owner, TattooUiKey.Editor, args.User);
    }

    private void OnPropose(Entity<TattooMachineComponent> ent, ref TattooProposeMessage args)
    {
        if (ent.Comp.Artist != args.Actor || ent.Comp.Pending != null)
            return;
        var tattoo = new TextTattoo { Text = FormattedMessage.RemoveMarkupPermissive(args.Text).Trim(), Zone = args.Zone, Color = args.Color };
        if (ent.Comp.Patient is not { } patient || !CanWork(ent, args.Actor, patient))
        {
            _popup.PopupEntity(Loc.GetString("tattoo-unavailable"), ent, args.Actor);
            return;
        }
        string? error = ent.Comp.Ink <= 0 ? "tattoo-no-ink" :
            !TextTattooRules.Valid(tattoo) ? "tattoo-invalid" :
            !_body.GetBodyChildren(patient).Any(p => TextTattooRules.Matches(tattoo, p.Component)) ? "tattoo-missing-part" :
            Comp<DossierHolderComponent>(patient).Record.Tattoos.Count >= TextTattooRules.MaximumTattoos ? "tattoo-limit" : null;
        if (error != null)
        {
            _popup.PopupEntity(Loc.GetString(error), ent, args.Actor);
            return;
        }
        ent.Comp.Pending = tattoo;
        ent.Comp.Accepted = false;
        ent.Comp.RequestId++;
        ent.Comp.ExpiresAt = _timing.CurTime + TimeSpan.FromMinutes(1);
        UpdateState(ent);
        if (!_ui.TryOpenUi(ent.Owner, TattooUiKey.Consent, patient))
        {
            ent.Comp.Pending = null;
            ent.Comp.RequestId++;
            UpdateState(ent);
            _popup.PopupEntity(Loc.GetString("tattoo-unavailable"), ent, args.Actor);
        }
    }

    private void OnConsentOpened(Entity<TattooMachineComponent> ent, ref BoundUIOpenedEvent args)
    {
        if (ent.Comp.Patient != args.Actor || ent.Comp.Pending == null ||
            ent.Comp.Artist is not { } artist || !CanWork(ent, artist, args.Actor))
            _ui.CloseUi(ent.Owner, TattooUiKey.Consent, args.Actor);
    }

    private void OnConsentClosed(Entity<TattooMachineComponent> ent, ref BoundUIClosedEvent args)
    {
        if (ent.Comp.Patient != args.Actor || ent.Comp.Pending == null)
            return;
        ent.Comp.Pending = null;
        ent.Comp.Accepted = false;
        ent.Comp.RequestId++;
        UpdateState(ent);
    }

    private void OnConsent(Entity<TattooMachineComponent> ent, ref TattooConsentMessage args)
    {
        if (ent.Comp.Patient != args.Actor || ent.Comp.Pending == null || ent.Comp.Artist is not { } artist)
            return;
        if (!args.Accepted)
        {
            ent.Comp.Pending = null;
            ent.Comp.Accepted = false;
            ent.Comp.RequestId++;
            _ui.CloseUi(ent.Owner, TattooUiKey.Consent);
            UpdateState(ent);
            return;
        }
        if (ent.Comp.Accepted || _timing.CurTime > ent.Comp.ExpiresAt || !CanWork(ent, artist, args.Actor))
            return;
        ent.Comp.Accepted = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, artist, 8,
            new TattooApplyEvent { RequestId = ent.Comp.RequestId }, ent, target: args.Actor, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
        UpdateState(ent);
    }

    private void OnFinished(Entity<TattooMachineComponent> ent, ref TattooApplyEvent args)
    {
        if (args.Handled || args.RequestId != ent.Comp.RequestId)
            return;
        args.Handled = true;
        if (!args.Cancelled && ent.Comp.Accepted && ent.Comp.Pending is { } tattoo && ent.Comp.Ink > 0 &&
            args.Target is { } patient && ent.Comp.Patient == patient && CanWork(ent, args.User, patient) &&
            _body.GetBodyChildren(patient).Any(p => TextTattooRules.Matches(tattoo, p.Component)))
        {
            var tattoos = Comp<DossierHolderComponent>(patient).Record.Tattoos;
            if (tattoos.Count < TextTattooRules.MaximumTattoos && _dossiers.SaveTattoos(patient, [.. tattoos, tattoo]))
            {
                ent.Comp.Ink--;
                _popup.PopupEntity(Loc.GetString("tattoo-complete"), patient, args.User);
            }
        }
        ent.Comp.Pending = null;
        ent.Comp.Accepted = false;
        _ui.CloseUi(ent.Owner, TattooUiKey.Consent);
        UpdateState(ent);
    }

    private void UpdateState(Entity<TattooMachineComponent> ent)
    {
        var state = new TattooUiState(ent.Comp.Patient is { } target ? Name(target) : "", ent.Comp.Ink,
            ent.Comp.Pending?.Clone(), ent.Comp.Accepted);
        _ui.SetUiState(ent.Owner, TattooUiKey.Editor, state);
        _ui.SetUiState(ent.Owner, TattooUiKey.Consent, state);
    }

    private void OnExamined(Entity<TextTattooComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;
        foreach (var tattoo in ent.Comp.Tattoos.Where(TextTattooRules.Valid))
        {
            var slot = tattoo.Zone == 0 ? "mask" : "jumpsuit";
            if (_inventory.TryGetSlotEntity(ent, slot, out _) ||
                _inventory.TryGetSlotEntity(ent, tattoo.Zone == 0 ? "head" : "outerClothing", out _))
                continue;
            var message = new FormattedMessage();
            message.PushColor(TextTattooRules.Colors[tattoo.Color]);
            message.AddText(Loc.GetString("tattoo-examine", ("zone", Loc.GetString("tattoo-zone-" + tattoo.Zone)), ("text", tattoo.Text)));
            message.Pop();
            args.PushMessage(message);
        }
    }
}

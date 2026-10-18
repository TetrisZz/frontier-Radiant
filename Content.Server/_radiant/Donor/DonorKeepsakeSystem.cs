using Content.Server._CorvaxGoob.Photo;
using Content.Shared._CorvaxGoob.Photo;
using Content.Shared._radiant.Donor;
using Content.Shared.ActionBlocker;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Donor;

public sealed class DonorKeepsakeSystem : EntitySystem
{
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<DonorKeepsakeStyleComponent, MapInitEvent>(OnStyleInit);
        SubscribeLocalEvent<DonorKeepsakeStyleComponent, GetVerbsEvent<AlternativeVerb>>(StyleVerbs);
        SubscribeLocalEvent<DonorKeepsakeStyleComponent, ExaminedEvent>(OnStyleExamined);
        SubscribeLocalEvent<DonorPairedBraceletComponent, InteractUsingEvent>(MatchBracelets);
        SubscribeLocalEvent<DonorStopwatchComponent, GetVerbsEvent<AlternativeVerb>>(StopwatchVerbs);
        SubscribeLocalEvent<DonorStopwatchComponent, ExaminedEvent>(StopwatchExamined);
        SubscribeLocalEvent<DonorPhotoHolderComponent, MapInitEvent>(PhotoInit);
        SubscribeLocalEvent<DonorPhotoHolderComponent, ItemToggledEvent>(PhotoToggled);
        SubscribeLocalEvent<DonorPhotoHolderComponent, GetVerbsEvent<AlternativeVerb>>(PhotoVerbs);
        SubscribeLocalEvent<DonorPhotoHolderComponent, EntRemovedFromContainerMessage>(PhotoRemoved);
    }

    private bool CanUse(EntityUid user, EntityUid item) => !Deleted(user) && !Deleted(item) &&
        _blocker.CanInteract(user, item) && _interaction.InRangeUnobstructed(user, item);

    private void OnStyleInit(Entity<DonorKeepsakeStyleComponent> ent, ref MapInitEvent args) => UpdateStyle(ent);

    private void UpdateStyle(Entity<DonorKeepsakeStyleComponent> ent)
    {
        if (ent.Comp.Styles.Length == 0)
            return;
        ent.Comp.Style = Math.Clamp(ent.Comp.Style, 0, ent.Comp.Styles.Length - 1);
        _appearance.SetData(ent, DonorKeepsakeVisuals.Style, ent.Comp.Style);
    }

    private void StyleVerbs(Entity<DonorKeepsakeStyleComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        var user = args.User;
        for (var i = 0; i < ent.Comp.Styles.Length; i++)
        {
            var style = i;
            args.Verbs.Add(new AlternativeVerb
            {
                Text = Loc.GetString("donor-keepsake-select-style", ("style", Loc.GetString(ent.Comp.Styles[i]))),
                Act = () =>
                {
                    if (!CanUse(user, ent))
                        return;
                    ent.Comp.Style = style;
                    UpdateStyle(ent);
                },
            });
        }
    }

    private void OnStyleExamined(Entity<DonorKeepsakeStyleComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;
        if (ent.Comp.Styles.Length > 0)
            args.PushText(Loc.GetString("donor-keepsake-style", ("style", Loc.GetString(ent.Comp.Styles[Math.Clamp(ent.Comp.Style, 0, ent.Comp.Styles.Length - 1)]))));
    }

    private void MatchBracelets(Entity<DonorPairedBraceletComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || args.Used == ent.Owner || !HasComp<DonorPairedBraceletComponent>(args.Used) ||
            !TryComp<DonorKeepsakeStyleComponent>(ent, out var target) ||
            !TryComp<DonorKeepsakeStyleComponent>(args.Used, out var source))
            return;
        args.Handled = true;
        target.Style = source.Style;
        UpdateStyle((ent.Owner, target));
        _popup.PopupEntity(Loc.GetString("donor-bracelets-matched"), ent, args.User);
    }

    public TimeSpan StopwatchElapsed(Entity<DonorStopwatchComponent> ent) => ent.Comp.Elapsed +
        (ent.Comp.Started is { } start ? _timing.CurTime - start : TimeSpan.Zero);

    public void ToggleStopwatch(Entity<DonorStopwatchComponent> ent)
    {
        if (ent.Comp.Started != null)
        {
            ent.Comp.Elapsed = StopwatchElapsed(ent);
            ent.Comp.Started = null;
        }
        else
            ent.Comp.Started = _timing.CurTime;
    }

    private void StopwatchVerbs(Entity<DonorStopwatchComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString(ent.Comp.Started == null ? "donor-stopwatch-start" : "donor-stopwatch-stop"),
            Act = () => { if (CanUse(user, ent)) ToggleStopwatch(ent); },
        });
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("donor-stopwatch-reset"),
            Act = () =>
            {
                if (!CanUse(user, ent)) return;
                ent.Comp.Elapsed = TimeSpan.Zero;
                ent.Comp.Started = null;
            },
        });
    }

    private void StopwatchExamined(Entity<DonorStopwatchComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange)
            args.PushText(Loc.GetString("donor-stopwatch-examine", ("time", StopwatchElapsed(ent).ToString(@"hh\:mm\:ss"))));
    }

    private void PhotoInit(Entity<DonorPhotoHolderComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Closeable)
            _slots.SetLock(ent, ent.Comp.Slot, !_toggle.IsActivated(ent.Owner));
    }

    private void PhotoToggled(Entity<DonorPhotoHolderComponent> ent, ref ItemToggledEvent args)
    {
        if (ent.Comp.Closeable)
            _slots.SetLock(ent, ent.Comp.Slot, !args.Activated);
        if (!args.Activated)
            _ui.CloseUi(ent.Owner, PhotoCardUiKey.Key);
    }

    private void PhotoRemoved(Entity<DonorPhotoHolderComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == ent.Comp.Slot)
            _ui.CloseUi(ent.Owner, PhotoCardUiKey.Key);
    }

    private void PhotoVerbs(Entity<DonorPhotoHolderComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.Closeable && !_toggle.IsActivated(ent.Owner) ||
            !_slots.TryGetSlot(ent, ent.Comp.Slot, out var slot) || slot.Item is not { } photo ||
            !TryComp<PhotoCardComponent>(photo, out var card))
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("donor-photo-view"),
            Act = () =>
            {
                if (!CanUse(user, ent) || slot.Item != photo || ent.Comp.Closeable && !_toggle.IsActivated(ent.Owner))
                    return;
                _ui.SetUiState(ent.Owner, PhotoCardUiKey.Key, new PhotoCardUiState(card.ImageData));
                _ui.TryOpenUi(ent.Owner, PhotoCardUiKey.Key, user);
            },
        });
    }
}

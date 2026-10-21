using System.Linq;
using Content.Server.GameTicking;
using Content.Server.Power.Components;
using Content.Shared._radiant.Paperwork;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Content.Shared.Power;
using Content.Shared.Power.Components;
using Content.Shared.Popups;
using Content.Shared.Stacks;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;
using Robust.Shared.Utility;

namespace Content.Server._radiant.Paperwork;

public sealed class PhotocopierSystem : EntitySystem
{
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly GameTicker _ticker = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly PaperSystem _paper = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;

    public override void Initialize()
    {
        Subs.BuiEvents<PhotocopierComponent>(PhotocopierUiKey.Key, subs =>
        {
            subs.Event<BoundUIOpenedEvent>((Entity<PhotocopierComponent> ent, ref BoundUIOpenedEvent _) => UpdateState(ent));
            subs.Event<PhotocopierPrintMessage>(OnPrint);
        });
        SubscribeLocalEvent<PhotocopierComponent, InteractUsingEvent>(OnRefill);
        SubscribeLocalEvent<PhotocopierComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<PhotocopierComponent, EntInsertedIntoContainerMessage>((Entity<PhotocopierComponent> ent, ref EntInsertedIntoContainerMessage _) => UpdateState(ent));
        SubscribeLocalEvent<PhotocopierComponent, EntRemovedFromContainerMessage>((Entity<PhotocopierComponent> ent, ref EntRemovedFromContainerMessage _) => UpdateState(ent));
    }

    private void OnPrint(Entity<PhotocopierComponent> ent, ref PhotocopierPrintMessage args)
    {
        if (args.Copies is < 1 or > 5 || _timing.CurTime < ent.Comp.ReadyAt ||
            !TryComp<ApcPowerReceiverComponent>(ent, out var power) || !power.Powered ||
            !_interaction.InRangeUnobstructed(args.Actor, ent.Owner))
            return;
        if (ent.Comp.Paper < args.Copies || ent.Comp.Ink < args.Copies)
        {
            _popup.PopupEntity(Loc.GetString("photocopier-no-supplies"), ent, args.Actor);
            UpdateState(ent);
            return;
        }

        string content;
        string title;
        PaperComponent? original = null;
        if (args.Form is { } formId)
        {
            if (!_prototypes.TryIndex<DocumentFormPrototype>(formId, out var form))
                return;
            if (!Loc.TryGetString(form.Name, out var formTitle) || !Loc.TryGetString(form.Content, out var body) ||
                !Loc.TryGetString(form.Letterhead, out var letterhead))
            {
                TemplateError(ent, args.Actor, formId);
                return;
            }
            title = formTitle;
            var grid = Transform(ent).GridUid;
            if (!Loc.TryGetString("photocopier-document-header", out var header,
                ("title", FormattedMessage.EscapeText(title)), ("date", PaperworkClock.Date(DateTimeOffset.UtcNow)),
                ("time", PaperworkClock.Shift(_ticker.RoundDuration())),
                ("ship", grid is { } uid ? FormattedMessage.EscapeText(MetaData(uid).EntityName) : "[form]")))
            {
                TemplateError(ent, args.Actor, formId);
                return;
            }
            content = letterhead + "\n\n" + header + "\n\n" + body;
        }
        else
        {
            var source = _slots.GetItemOrNull(ent, "original");
            if (source is not { } sourceUid || !TryComp(sourceUid, out original))
                return;
            title = Loc.GetString("photocopier-copy-name", ("name", MetaData(sourceUid).EntityName));
            content = original.Content;
        }

        for (var i = 0; i < args.Copies; i++)
        {
            var sheet = Spawn("Paper", Transform(ent).Coordinates);
            _metadata.SetEntityName(sheet, title);
            _paper.SetContent(sheet, content);
            if (original != null)
            {
                var copy = Comp<PaperComponent>(sheet);
                copy.Language = original.Language;
                copy.StampedBy = new(original.StampedBy);
                copy.StampState = original.StampState;
                copy.StampRsiPath = original.StampRsiPath;
                copy.EditingDisabled = original.EditingDisabled;
                _appearance.SetData(sheet, PaperComponent.PaperVisuals.Stamp, copy.StampState ?? "");
                _appearance.SetData(sheet, PaperComponent.PaperVisuals.StampRsi, copy.StampRsiPath ?? "");
                Dirty(sheet, copy);
            }
        }
        ent.Comp.Paper -= args.Copies;
        ent.Comp.Ink -= args.Copies;
        ent.Comp.ReadyAt = _timing.CurTime + TimeSpan.FromSeconds(args.Copies * 1.5);
        _audio.PlayPvs(new SoundCollectionSpecifier("PrinterPrint"), ent);
        UpdateState(ent);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<PhotocopierComponent>();
        while (query.MoveNext(out var uid, out var printer))
        {
            if (printer.ReadyAt == TimeSpan.Zero || printer.ReadyAt > _timing.CurTime)
                continue;
            printer.ReadyAt = TimeSpan.Zero;
            UpdateState((uid, printer));
        }
    }

    private void OnRefill(Entity<PhotocopierComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled)
            return;
        if (TryComp<StackComponent>(args.Used, out var stack) && stack.StackTypeId == "Paper")
        {
            args.Handled = true;
            var count = Math.Min(_stacks.GetCount(args.Used, stack), ent.Comp.Capacity - ent.Comp.Paper);
            if (count > 0 && _stacks.Use(args.Used, count, stack))
                ent.Comp.Paper += count;
        }
        else if (TryComp<PrinterTonerComponent>(args.Used, out var toner))
        {
            args.Handled = true;
            var amount = Math.Min(toner.Ink, ent.Comp.InkCapacity - ent.Comp.Ink);
            ent.Comp.Ink += amount;
            toner.Ink -= amount;
            if (toner.Ink <= 0)
                QueueDel(args.Used);
        }
        else
            return;
        UpdateState(ent);
    }

    private void OnExamine(Entity<PhotocopierComponent> ent, ref ExaminedEvent args)
        => args.PushText(Loc.GetString("photocopier-supplies", ("paper", ent.Comp.Paper), ("ink", ent.Comp.Ink)));

    private void TemplateError(Entity<PhotocopierComponent> ent, EntityUid user, string form)
    {
        Log.Error($"Unable to resolve photocopier template {form}; printing cancelled.");
        _popup.PopupEntity(Loc.GetString("photocopier-template-error"), ent, user);
    }

    private void UpdateState(Entity<PhotocopierComponent> ent)
    {
        var forms = _prototypes.EnumeratePrototypes<DocumentFormPrototype>()
            .Select(f => new DocumentFormEntry(f.ID, Loc.GetString(f.Name), Loc.GetString(f.Category),
                Loc.GetString(f.Letterhead) + "\n\n[head=2]" + FormattedMessage.EscapeText(Loc.GetString(f.Name)) +
                "[/head]\n\n" + Loc.GetString(f.Content)))
            .OrderBy(f => f.Category).ThenBy(f => f.Name).ToList();
        _ui.SetUiState(ent.Owner, PhotocopierUiKey.Key, new PhotocopierUiState(forms, ent.Comp.Paper, ent.Comp.Ink,
            _slots.GetItemOrNull(ent, "original") != null, ent.Comp.ReadyAt <= _timing.CurTime));
    }
}

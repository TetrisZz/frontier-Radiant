using System.Linq;
using Content.Shared._radiant.Social;
using Content.Shared.Clothing;
using Content.Shared.Containers.ItemSlots;
using Content.Shared.Examine;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Interaction;
using Content.Shared.Paper;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Content.Shared._Goobstation.Languages;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction.Events;
using Content.Shared.ActionBlocker;
using Robust.Server.GameObjects;
using Robust.Shared.Utility;

namespace Content.Server._radiant.Social;

public sealed class SocialItemSystem : EntitySystem
{
    [Dependency] private readonly ItemSlotsSystem _slots = default!;
    [Dependency] private readonly SharedHumanoidAppearanceSystem _humanoid = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly MarkingManager _markings = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BouquetNoteComponent, ExaminedEvent>(OnBouquetExamined);
        SubscribeLocalEvent<StoryRelicComponent, ExaminedEvent>((Entity<StoryRelicComponent> ent, ref ExaminedEvent args) =>
        {
            if (args.IsInDetailsRange)
                args.PushText(Loc.GetString(ent.Comp.Story));
        });
        SubscribeLocalEvent<HairAccessoryComponent, ClothingGotEquippedEvent>(OnEquipped);
        SubscribeLocalEvent<HairAccessoryComponent, ClothingGotUnequippedEvent>(OnUnequipped);
        SubscribeLocalEvent<HairAccessoryComponent, ComponentShutdown>((Entity<HairAccessoryComponent> ent, ref ComponentShutdown _) => Restore(ent));
        SubscribeLocalEvent<HairAccessoryComponent, GetVerbsEvent<AlternativeVerb>>(OnStyles);
        SubscribeLocalEvent<HairAccessoryComponent, UseInHandEvent>(OnUseAccessory);
        Subs.BuiEvents<HairAccessoryComponent>(HairAccessoryUiKey.Key,
            s => s.Event<HairAccessorySelectMessage>(OnSelectStyle));
    }

    private void OnBouquetExamined(Entity<BouquetNoteComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || _slots.GetItemOrNull(ent, "note") is not { } note ||
            !TryComp<PaperComponent>(note, out var paper))
            return;
        if (!HasComp<GhostComponent>(args.Examiner) && (paper.Language == null
                ? HasComp<NativeLanguageOnlyComponent>(args.Examiner)
                : HasComp<NativeLanguageUnfamiliarComponent>(args.Examiner) ||
                  SpeciesLanguageUtility.GetNativeLanguage(EntityManager, args.Examiner) != paper.Language))
        {
            args.PushText(Loc.GetString("bouquet-note-unreadable"));
            return;
        }
        var text = FormattedMessage.RemoveMarkupPermissive(paper.Content).Trim();
        args.PushText(Loc.GetString(text.Length == 0 ? "bouquet-note-blank" : "bouquet-note-text",
            ("text", text.Length > 1000 ? text[..1000] + "…" : text)));
    }

    private void OnEquipped(Entity<HairAccessoryComponent> ent, ref ClothingGotEquippedEvent args)
    {
        if (ent.Comp.Wearer == args.Wearer && ent.Comp.Original != null)
            return;
        if (!TryComp<HumanoidAppearanceComponent>(args.Wearer, out var appearance) ||
            !appearance.MarkingSet.Markings.TryGetValue(MarkingCategories.Hair, out var hair) || hair.Count == 0 ||
            hair.All(m => m.MarkingId == HairStyles.DefaultHairStyle.Id))
        {
            _popup.PopupEntity(Loc.GetString("hair-accessory-no-hair"), args.Wearer, args.Wearer);
            return;
        }
        ent.Comp.Wearer = args.Wearer;
        ent.Comp.Original = hair.Select(m => new Marking(m)).ToList();
        Apply(ent, args.Wearer, appearance);
    }

    private void Apply(Entity<HairAccessoryComponent> ent, EntityUid wearer, HumanoidAppearanceComponent appearance)
    {
        if (!_markings.Markings.TryGetValue(ent.Comp.Style, out var prototype) ||
            !_markings.CanBeApplied(appearance.Species, appearance.Sex, prototype))
            return;
        var color = appearance.CachedHairColor ?? ent.Comp.Original?.FirstOrDefault()?.MarkingColors.FirstOrDefault() ?? Color.Black;
        appearance.MarkingSet.RemoveCategory(MarkingCategories.Hair);
        _humanoid.AddMarking(wearer, ent.Comp.Style, color, humanoid: appearance);
        ent.Comp.AppliedStyle = ent.Comp.Style;
    }

    private void OnUnequipped(Entity<HairAccessoryComponent> ent, ref ClothingGotUnequippedEvent args) => Restore(ent);

    public void Restore(Entity<HairAccessoryComponent> ent)
    {
        if (ent.Comp.Wearer is { } wearer && ent.Comp.Original is { } original &&
            !TerminatingOrDeleted(wearer) &&
            TryComp<HumanoidAppearanceComponent>(wearer, out var appearance) &&
            appearance.MarkingSet.Markings.TryGetValue(MarkingCategories.Hair, out var current) &&
            current.Count == 1 && current[0].MarkingId == ent.Comp.AppliedStyle)
        {
            appearance.MarkingSet.RemoveCategory(MarkingCategories.Hair);
            foreach (var marking in original)
                appearance.MarkingSet.AddBack(MarkingCategories.Hair, new Marking(marking));
            Dirty(wearer, appearance);
        }
        ent.Comp.Wearer = null;
        ent.Comp.Original = null;
        ent.Comp.AppliedStyle = null;
    }

    private void OnStyles(Entity<HairAccessoryComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || ent.Comp.Wearer is { } wearer && wearer != args.User)
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("hair-accessory-open"),
            Act = () => OpenAccessory(ent, user),
        });
    }

    private bool CanStyle(Entity<HairAccessoryComponent> ent, EntityUid user)
        => _blocker.CanInteract(user, ent) && (ent.Comp.Wearer == user ||
            ent.Comp.Wearer == null && _hands.IsHolding(user, ent)) &&
            _interaction.IsAccessible(user, ent.Owner);

    private List<string> AvailableStyles(Entity<HairAccessoryComponent> ent, HumanoidAppearanceComponent appearance)
        => ent.Comp.Styles.Where(id => _markings.Markings.TryGetValue(id, out var marking) &&
            _markings.CanBeApplied(appearance.Species, appearance.Sex, marking)).ToList();

    private void OpenAccessory(Entity<HairAccessoryComponent> ent, EntityUid user)
    {
        if (!CanStyle(ent, user) || !TryComp<HumanoidAppearanceComponent>(user, out var appearance))
            return;
        _ui.SetUiState(ent.Owner, HairAccessoryUiKey.Key,
            new HairAccessoryUiState(AvailableStyles(ent, appearance), ent.Comp.Style));
        _ui.TryOpenUi(ent.Owner, HairAccessoryUiKey.Key, user);
    }

    private void OnUseAccessory(Entity<HairAccessoryComponent> ent, ref UseInHandEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        OpenAccessory(ent, args.User);
    }

    private void OnSelectStyle(Entity<HairAccessoryComponent> ent, ref HairAccessorySelectMessage args)
    {
        if (!CanStyle(ent, args.Actor) || !TryComp<HumanoidAppearanceComponent>(args.Actor, out var appearance) ||
            !AvailableStyles(ent, appearance).Contains(args.Style))
            return;
        ent.Comp.Style = args.Style;
        if (ent.Comp.Wearer == args.Actor)
            Apply(ent, args.Actor, appearance);
        _ui.SetUiState(ent.Owner, HairAccessoryUiKey.Key,
            new HairAccessoryUiState(AvailableStyles(ent, appearance), ent.Comp.Style));
    }
}

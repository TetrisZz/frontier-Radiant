using Content.Server.Forensics;
using Content.Server.Atmos.Components;
using Content.Server.Atmos.Piping.Unary.Components;
using Content.Server.Botany.Components;
using Content.Server._DV.Mail.Components;
using Content.Shared._radiant.Paint;
using Content.Shared._radiant.Passports;
using Content.Shared._EE.Contractors.Components;
using Content.Shared.Access.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Medical.Healing;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Clothing.Components;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Item.ItemToggle.Components;
using Content.Shared.Kitchen.Components;
using Content.Shared.Tools.Components;
using Content.Shared.Weapons.Melee;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Mobs.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Popups;
using Content.Shared.Paper;
using Content.Shared.PDA;
using Content.Shared.Pinpointer;
using Content.Shared.Radio.Components;
using Content.Shared.Tag;
using Content.Shared.Storage.EntitySystems;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Paint;

public sealed class WashablePaintSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly TagSystem _tags = default!;
    private static readonly ProtoId<TagPrototype> BookTag = "Book";
    private static readonly ProtoId<TagPrototype> TrashTag = "Trash";
    private static readonly ProtoId<TagPrototype> NoWashablePaintTag = "NoWashablePaint";
    private static readonly ProtoId<TagPrototype> PillCanisterTag = "PillCanister";

    public override void Initialize()
    {
        SubscribeLocalEvent<RandomPaintColorComponent, MapInitEvent>(OnRandomPaintInit);
        SubscribeLocalEvent<PaintCanComponent, AfterInteractEvent>(OnPaint);
        SubscribeLocalEvent<PaintCanComponent, PaintItemAttemptEvent>(OnPaintAttempt);
        SubscribeLocalEvent<PaintCanComponent, ApplyWashablePaintEvent>(OnPaintComplete);
        SubscribeLocalEvent<PaintCanComponent, ExaminedEvent>(OnCanExamined);
        SubscribeLocalEvent<WashablePaintComponent, ExaminedEvent>(OnPaintExamined);
        SubscribeLocalEvent<WashablePaintComponent, InteractUsingEvent>(OnSoap,
            before: new[] { typeof(SharedStorageSystem) });
        SubscribeLocalEvent<WashablePaintComponent, WashPaintEvent>(OnSoapComplete);
        SubscribeLocalEvent<WashablePaintComponent, ReactionEntityEvent>(OnWater);
        SubscribeLocalEvent<PaintWashStationComponent, InteractUsingEvent>(OnSink,
            before: new[] { typeof(SharedStorageSystem) });
        SubscribeLocalEvent<PaintWashStationComponent, WashPaintEvent>(OnSinkComplete);
    }

    private void OnRandomPaintInit(Entity<RandomPaintColorComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Initialized || ent.Comp.Colors.Count == 0 || !TryComp<PaintCanComponent>(ent, out var paint))
            return;
        paint.Color = _random.Pick(ent.Comp.Colors);
        ent.Comp.Initialized = true;
        Dirty(ent.Owner, paint);
    }

    public bool Paint(EntityUid target, Color color)
    {
        if (!CanPaint(target))
            return false;
        var paint = EnsureComp<WashablePaintComponent>(target);
        paint.Color = color;
        Dirty(target, paint);
        return true;
    }

    public bool CanPaint(EntityUid target) =>
        HasComp<ItemComponent>(target) && !HasComp<MobStateComponent>(target) &&
        !HasComp<GasVentPumpComponent>(target) && !HasComp<GasVentScrubberComponent>(target) &&
        !HasComp<GasPassiveVentComponent>(target) &&
        // Cloth is edible with special digestion, but is not ordinary food.
        !(TryComp<FoodComponent>(target, out var food) &&
          !(food.RequiresSpecialDigestion && HasComp<ClothingComponent>(target))) &&
        !(TryComp<EdibleComponent>(target, out var edible) &&
          !(edible.RequiresSpecialDigestion && HasComp<ClothingComponent>(target))) &&
        !_tags.HasTag(target, TrashTag) &&
        !_tags.HasTag(target, NoWashablePaintTag) && !HasComp<MailComponent>(target) &&
        !HasComp<SeedComponent>(target) && !HasComp<ProduceComponent>(target) &&
        !HasComp<PlantHolderComponent>(target) &&
        !HasComp<EncryptionKeyComponent>(target) && !HasComp<StationMapComponent>(target) &&
        !HasComp<HealingComponent>(target) && !HasComp<PillComponent>(target) &&
        !HasComp<HyposprayComponent>(target) && !_tags.HasTag(target, PillCanisterTag) &&
        !HasComp<StampComponent>(target) &&
        !HasComp<PressureProtectionComponent>(target) && !HasComp<GunComponent>(target) &&
        !HasComp<PdaComponent>(target) && !HasComp<PaperComponent>(target) &&
        !HasComp<PassportComponent>(target) && !HasComp<ServiceCredentialComponent>(target) &&
        !HasComp<IdCardComponent>(target) && !_tags.HasTag(target, BookTag) &&
        // Blades may also be slicing tools; ordinary engineering tools remain paintable.
        !(TryComp<MeleeWeaponComponent>(target, out var melee) &&
          (HasComp<SharpComponent>(target) || (!melee.Hidden && !HasComp<ToolComponent>(target))));

    public void Wash(EntityUid target) => RemComp<WashablePaintComponent>(target);

    private void OnPaint(Entity<PaintCanComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || target == ent.Owner ||
            !HasComp<ItemComponent>(target) || HasComp<MobStateComponent>(target))
            return;
        args.Handled = TryStartPainting(ent, args.User, target);
    }

    private void OnPaintAttempt(Entity<PaintCanComponent> ent, ref PaintItemAttemptEvent args) =>
        TryStartPainting(ent, args.User, args.Target);

    private bool TryStartPainting(Entity<PaintCanComponent> ent, EntityUid user, EntityUid target)
    {
        if (!_interaction.InRangeUnobstructed(user, target))
            return false;
        if (!CanPaint(target))
        {
            _popup.PopupEntity(Loc.GetString("paint-target-protected"), target, user);
            return true;
        }
        if (!TryComp<ItemToggleComponent>(ent, out var toggle) || !toggle.Activated)
        {
            _popup.PopupEntity(Loc.GetString("paint-can-capped"), ent, user);
            return true;
        }
        if (ent.Comp.Uses <= 0)
        {
            _popup.PopupEntity(Loc.GetString("paint-can-empty"), ent, user);
            return true;
        }
        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user,
            TimeSpan.FromSeconds(1.5), new ApplyWashablePaintEvent(), ent, target: target, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnPaintComplete(Entity<PaintCanComponent> ent, ref ApplyWashablePaintEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target is not { } target || ent.Comp.Uses <= 0 ||
            !TryComp<ItemToggleComponent>(ent, out var toggle) || !toggle.Activated ||
            !_interaction.InRangeUnobstructed(args.User, target) || !Paint(target, ent.Comp.Color))
            return;
        ent.Comp.Uses--;
        Dirty(ent);
        args.Handled = true;
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/spray.ogg"), target);
    }

    private void OnCanExamined(Entity<PaintCanComponent> ent, ref ExaminedEvent args) =>
        args.PushText(Loc.GetString("paint-can-uses", ("uses", ent.Comp.Uses)));

    private void OnPaintExamined(Entity<WashablePaintComponent> ent, ref ExaminedEvent args) =>
        args.PushText(Loc.GetString("paint-coated"));

    private void OnSoap(Entity<WashablePaintComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<CleansForensicsComponent>(args.Used))
            return;
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User,
            TimeSpan.FromSeconds(3), new WashPaintEvent(), ent, target: ent, used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnSoapComplete(Entity<WashablePaintComponent> ent, ref WashPaintEvent args)
    {
        if (args.Cancelled || args.Handled || !HasComp<CleansForensicsComponent>(args.Used))
            return;
        Wash(ent);
        args.Handled = true;
    }

    private void OnWater(Entity<WashablePaintComponent> ent, ref ReactionEntityEvent args)
    {
        if (args.Method == ReactionMethod.Touch && args.Reagent.ID is "Water" or "SpaceCleaner" && args.ReagentQuantity.Quantity >= 5)
            Wash(ent);
    }

    private void OnSink(Entity<PaintWashStationComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<WashablePaintComponent>(args.Used))
            return;
        if (!HasWater(ent))
        {
            _popup.PopupEntity(Loc.GetString("paint-sink-no-water"), ent, args.User);
            args.Handled = true;
            return;
        }
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User,
            TimeSpan.FromSeconds(2), new WashPaintEvent(), ent, target: ent, used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    public bool HasWater(EntityUid sink) => _solutions.TryGetSolution(sink, "tank", out _, out var solution) &&
        solution.GetTotalPrototypeQuantity("Water") >= 5;

    public bool TryWashAtSink(EntityUid sink, EntityUid item)
    {
        if (!HasComp<PaintWashStationComponent>(sink) || !HasComp<WashablePaintComponent>(item) ||
            !_solutions.TryGetSolution(sink, "tank", out var tank, out var solution) ||
            solution.GetTotalPrototypeQuantity("Water") < 5)
            return false;
        _solutions.RemoveReagent(tank.Value, "Water", 5);
        Wash(item);
        return true;
    }

    private void OnSinkComplete(Entity<PaintWashStationComponent> ent, ref WashPaintEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } item ||
            !_interaction.InRangeUnobstructed(args.User, ent.Owner) || !TryWashAtSink(ent, item))
            return;
        args.Handled = true;
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Effects/Fluids/watersplash.ogg"), ent);
    }
}

using System.Linq;
using Content.Server.Power.EntitySystems;
using Content.Shared._radiant.Artisan;
using Content.Shared._radiant.Paint;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Labels.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Audio;
using Content.Shared.Stacks;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Audio.Systems;

namespace Content.Server._radiant.Artisan;

public sealed partial class ArtisanSystem : SharedArtisanSystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly IPrototypeManager _prototypes = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;
    [Dependency] private readonly LabelSystem _labels = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    private bool _accessingTank;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<ArtisanStationComponent, ComponentStartup>((Entity<ArtisanStationComponent> ent, ref ComponentStartup args) =>
            _containers.EnsureContainer<Container>(ent, ArtisanStationComponent.Container));
        SubscribeLocalEvent<ArtisanToolComponent, ComponentStartup>((Entity<ArtisanToolComponent> ent, ref ComponentStartup args) =>
            _containers.EnsureContainer<Container>(ent, ArtisanToolComponent.Container));
        SubscribeLocalEvent<ArtisanStationComponent, MapInitEvent>(OnInit, after: new[] { typeof(SharedSolutionContainerSystem) });
        SubscribeLocalEvent<ArtisanStationComponent, AfterInteractUsingEvent>(OnInsertFailed);
        SubscribeLocalEvent<ArtisanStationComponent, BoundUIOpenedEvent>((Entity<ArtisanStationComponent> ent, ref BoundUIOpenedEvent args) => UpdateUi(ent));
        SubscribeLocalEvent<ArtisanToolComponent, EntInsertedIntoContainerMessage>((Entity<ArtisanToolComponent> ent, ref EntInsertedIntoContainerMessage args) =>
            _appearance.SetData(ent, ArtisanVisuals.Loaded, ToolItems(ent).Any()));
        SubscribeLocalEvent<ArtisanToolComponent, EntRemovedFromContainerMessage>((Entity<ArtisanToolComponent> ent, ref EntRemovedFromContainerMessage args) =>
            _appearance.SetData(ent, ArtisanVisuals.Loaded, ToolItems(ent).Any()));
        SubscribeLocalEvent<ArtisanStationComponent, ArtisanActionMessage>(OnAction);
        SubscribeLocalEvent<ArtisanStationComponent, ContainerIsInsertingAttemptEvent>((uid, comp, args) =>
        { if (args.Container.ID == ArtisanStationComponent.Container && (comp.Busy || args.Container.ContainedEntities.Count >= comp.Capacity)) args.Cancel(); });
        SubscribeLocalEvent<ArtisanStationComponent, ContainerIsRemovingAttemptEvent>((uid, comp, args) =>
        { if (args.Container.ID == ArtisanStationComponent.Container && comp.Busy && !TerminatingOrDeleted(uid)) args.Cancel(); });
        SubscribeLocalEvent<ArtisanStationComponent, SolutionAccessAttemptEvent>((Entity<ArtisanStationComponent> ent, ref SolutionAccessAttemptEvent args) =>
        { if (args.SolutionName == ArtisanStationComponent.Solution && ent.Comp.Busy && !_accessingTank) args.Cancelled = true; });
        SubscribeLocalEvent<ArtisanStationComponent, SolutionContainerChangedEvent>((Entity<ArtisanStationComponent> ent, ref SolutionContainerChangedEvent args) => UpdateUi(ent));
        SubscribeLocalEvent<ArtisanWorkpieceComponent, ExaminedEvent>(OnWorkpieceExamined);
        SubscribeLocalEvent<ArtisanProductComponent, ExaminedEvent>((uid, comp, args) =>
        { if (comp.Maker != "") args.PushText(Loc.GetString("artisan-maker", ("name", comp.Maker))); });
        SubscribeLocalEvent<ArtisanToolComponent, ExaminedEvent>((uid, comp, args) =>
        {
            foreach (var item in ToolItems(uid)) args.PushText(Name(item));
        });
    }

    private void OnInit(Entity<ArtisanStationComponent> ent, ref MapInitEvent args)
    {
        if (TryTank(ent, out var tank, out var solution))
        {
            solution.CanReact = false;
            _solutions.UpdateChemicals(tank, needsReactionsProcessing: false);
        }
        UpdateUi(ent);
        UpdateOperation(ent);
    }

    private IEnumerable<EntityUid> Inputs(EntityUid uid) => _containers.GetContainer(uid, ArtisanStationComponent.Container).ContainedEntities;
    private IEnumerable<EntityUid> ToolItems(EntityUid uid) => _containers.GetContainer(uid, ArtisanToolComponent.Container).ContainedEntities;

    private bool TryTank(EntityUid uid, out Entity<SolutionComponent> tank, out Solution solution)
    {
        var previous = _accessingTank;
        _accessingTank = true;
        try
        {
            var found = _solutions.TryGetSolution(uid, ArtisanStationComponent.Solution, out var resolved, out var contents);
            tank = resolved.GetValueOrDefault();
            solution = contents!;
            return found;
        }
        finally { _accessingTank = previous; }
    }

    private bool Ready(Entity<ArtisanStationComponent> ent) => Transform(ent).Anchored && (!ent.Comp.NeedsPower || _power.IsPowered(ent));
    private bool HeldTool(EntityUid user, ArtisanToolKind kind) => kind == ArtisanToolKind.None ||
        _hands.TryGetActiveItem(user, out var held) && TryComp<ArtisanToolComponent>(held, out var tool) && tool.Kind == kind;

    private bool Reject(EntityUid station, EntityUid user, string reason, params (string, object)[] args)
    {
        _popup.PopupEntity(Loc.GetString(reason, args), station, user);
        return false;
    }

    private void OnInsertFailed(Entity<ArtisanStationComponent> ent, ref AfterInteractUsingEvent args)
    {
        // Only report after ordinary interactions (pouring, painting, wrenching, etc.) have had a chance to succeed.
        if (args.Handled || !args.CanReach) return;
        var reason = ent.Comp.Busy ? "artisan-busy" :
            Inputs(ent).Count() >= ent.Comp.Capacity ? "artisan-input-full" :
            TryComp<ArtisanToolComponent>(args.Used, out var tool) && tool.Kind == ArtisanToolKind.Tongs ?
                ToolItems(args.Used).Any() ? "artisan-unsuitable-item" : "artisan-no-hot-workpiece" :
            Accepts(ent, args.Used) ? "artisan-cannot-load" : "artisan-unsuitable-item";
        Reject(ent, args.User, reason);
        args.Handled = true;
    }

    private bool Accepts(Entity<ArtisanStationComponent> ent, EntityUid item)
    {
        if (ent.Comp.Kind == ArtisanStationKind.Bottler)
            return _solutions.TryGetFitsInDispenser(item, out _, out _);
        if (HasComp<ArtisanWorkpieceComponent>(item)) return true;
        return _prototypes.EnumeratePrototypes<ArtisanRecipePrototype>().Any(recipe =>
            recipe.Steps[0].Station == ent.Comp.Kind && (recipe.Ingredients.Any(id => MetaData(item).EntityPrototype?.ID == id.Id) ||
            recipe.Stack != null && TryComp<StackComponent>(item, out var stack) && stack.StackTypeId == recipe.Stack));
    }

    protected override void OnInteractUsing(Entity<ArtisanStationComponent> ent, ref InteractUsingEvent args)
    {
        base.OnInteractUsing(ent, ref args);
        if (args.Handled || ent.Comp.Busy) return;
        var container = _containers.GetContainer(ent, ArtisanStationComponent.Container);
        if (TryComp<ArtisanToolComponent>(args.Used, out var tool) && tool.Kind == ArtisanToolKind.Tongs)
        {
            var held = ToolItems(args.Used).FirstOrDefault();
            if (Exists(held))
            {
                if (Accepts(ent, held) && _containers.Insert(held, container)) args.Handled = true;
            }
            else
            {
                var hot = Inputs(ent).FirstOrDefault(item => TryComp<ArtisanWorkpieceComponent>(item, out var piece) && piece.Heat > 0);
                if (Exists(hot) && _containers.Insert(hot, _containers.GetContainer(args.Used, ArtisanToolComponent.Container))) args.Handled = true;
            }
        }
        else if (Accepts(ent, args.Used) && _hands.TryDropIntoContainer(args.User, args.Used, container))
            args.Handled = true;
        if (args.Handled) UpdateUi(ent);
    }

    private void OnAction(Entity<ArtisanStationComponent> ent, ref ArtisanActionMessage msg)
    {
        if (!_interaction.InRangeUnobstructed(msg.Actor, ent.Owner, popup: true)) return;
        if (msg.Action == ArtisanAction.Stop)
        { Cancel(ent); UpdateUi(ent); return; }
        if (ent.Comp.Busy) { Reject(ent, msg.Actor, "artisan-busy"); return; }
        var item = msg.Item is { } net && TryGetEntity(net, out var resolved) && resolved.HasValue ? resolved.Value : (EntityUid?)null;
        if (msg.Item != null && (item == null || !Inputs(ent).Contains(item.Value)))
        { Reject(ent, msg.Actor, "artisan-missing-item"); return; }
        switch (msg.Action)
        {
            case ArtisanAction.Recipe:
                if (_prototypes.TryIndex<ArtisanRecipePrototype>(msg.Recipe, out var recipe)) Start(ent, msg.Actor, recipe, null);
                break;
            case ArtisanAction.Process:
                if (item is { } pieceUid && TryComp<ArtisanWorkpieceComponent>(pieceUid, out var piece) && _prototypes.TryIndex(piece.Recipe, out var definition))
                    Start(ent, msg.Actor, definition, pieceUid);
                else Reject(ent, msg.Actor, "artisan-missing-workpiece");
                break;
            case ArtisanAction.Reheat:
                if (item is not { } hot || !HasComp<ArtisanWorkpieceComponent>(hot))
                    Reject(ent, msg.Actor, "artisan-missing-workpiece");
                else if (ent.Comp.Kind != ArtisanStationKind.Furnace) Reject(ent, msg.Actor, "artisan-wrong-station");
                else if (!Ready(ent)) Reject(ent, msg.Actor, Transform(ent).Anchored ? "artisan-no-power" : "artisan-unanchored");
                else if (ent.Comp.Temperature < 700) Reject(ent, msg.Actor, "artisan-warming");
                else
                {
                    ent.Comp.Workpiece = hot; ent.Comp.Operator = msg.Actor; ent.Comp.Reheating = true;
                    ent.Comp.Remaining = ent.Comp.Duration = 5;
                }
                break;
            case ArtisanAction.Eject:
                if (item is { } eject) Eject(ent, msg.Actor, eject);
                break;
            case ArtisanAction.Source:
                if (ent.Comp.Kind == ArtisanStationKind.Bottler && item is { } source &&
                    _solutions.TryGetFitsInDispenser(source, out _, out var contents) && contents.Volume > 0)
                    ent.Comp.LiquidSource = item;
                else Reject(ent, msg.Actor, "artisan-no-source");
                break;
            case ArtisanAction.Settings:
                ApplySettings(ent, msg);
                break;
            case ArtisanAction.Bottle:
                if (msg.ApplySettings) ApplySettings(ent, msg);
                if (StartBottling(ent) is { } reason) Reject(ent, msg.Actor, reason);
                break;
            case ArtisanAction.ToggleTap:
                if (ent.Comp.Kind == ArtisanStationKind.Fermenter) SetTapMode(ent, !ent.Comp.DrainMode);
                break;
        }
        UpdateUi(ent);
    }

    private void ApplySettings(Entity<ArtisanStationComponent> ent, ArtisanActionMessage msg)
    {
        ent.Comp.BatchName = string.Concat(msg.BatchName.Where(c => !char.IsControl(c))).Trim();
        if (ent.Comp.BatchName.Length > 48) ent.Comp.BatchName = ent.Comp.BatchName[..48];
        ent.Comp.BottleAmount = Math.Clamp(msg.Amount, 1, 100);
    }

    private void Eject(Entity<ArtisanStationComponent> ent, EntityUid user, EntityUid item)
    {
        if (TryComp<ArtisanWorkpieceComponent>(item, out var piece) && piece.Heat > 0)
        {
            if (!_hands.TryGetActiveItem(user, out var held) || !TryComp<ArtisanToolComponent>(held, out var tool) ||
                tool.Kind != ArtisanToolKind.Tongs)
            { Reject(ent, user, "artisan-needed-tool", ("tool", Loc.GetString("artisan-tool-tongs"))); return; }
            if (ToolItems(held.Value).Any()) { Reject(ent, user, "artisan-tongs-full"); return; }
            _containers.Insert(item, _containers.GetContainer(held.Value, ArtisanToolComponent.Container));
            return;
        }
        _containers.Remove(item, _containers.GetContainer(ent, ArtisanStationComponent.Container));
        if (ent.Comp.LiquidSource == item) ent.Comp.LiquidSource = null;
        _transform.SetCoordinates(item, Transform(ent).Coordinates);
        _hands.TryPickupAnyHand(user, item, animate: false);
    }

    private void OnWorkpieceExamined(Entity<ArtisanWorkpieceComponent> ent, ref ExaminedEvent args)
    {
        if (!_prototypes.TryIndex(ent.Comp.Recipe, out var recipe)) return;
        if (ent.Comp.Stage < recipe.Steps.Count)
            args.PushText(Loc.GetString("artisan-next-step", ("step", Loc.GetString(recipe.Steps[ent.Comp.Stage].Name))));
        if (recipe.Steps[0].Station != ArtisanStationKind.Sculpting)
            args.PushText(Loc.GetString(recipe.Steps[0].Station == ArtisanStationKind.Furnace ? ent.Comp.Heat > 0 ? "artisan-hot" : "artisan-cold" : "artisan-unfired",
                ("seconds", (int)Math.Ceiling(ent.Comp.Heat))));
    }

    private void Cancel(Entity<ArtisanStationComponent> ent)
    {
        ent.Comp.Remaining = 0; ent.Comp.Recipe = null; ent.Comp.Workpiece = null;
        ent.Comp.Operator = null; ent.Comp.Reheating = false; ent.Comp.Bottling = false;
        UpdateOperation(ent);
    }

    private void SetTapMode(Entity<ArtisanStationComponent> ent, bool drain)
    {
        if (ent.Comp.DrainMode == drain) return;
        ent.Comp.DrainMode = drain;
        Dirty(ent);
        _audio.PlayPvs(drain ? ent.Comp.DrainModeSound : ent.Comp.FillModeSound, ent);
    }

    private void UpdateOperation(Entity<ArtisanStationComponent> ent)
    {
        if (ent.Comp.LiquidLocked != ent.Comp.Busy)
        {
            ent.Comp.LiquidLocked = ent.Comp.Busy;
            Dirty(ent);
        }
        _ambient.SetAmbience(ent, ent.Comp.Busy && Ready(ent));
    }

    public override void Update(float frameTime)
    {
        var pieces = EntityQueryEnumerator<ArtisanWorkpieceComponent>();
        while (pieces.MoveNext(out var uid, out var piece))
        {
            if (MetaData(uid).EntityPaused || piece.Heat <= 0) continue;
            piece.Heat = Math.Max(0, piece.Heat - frameTime);
            if ((int)piece.Heat != (int)(piece.Heat + frameTime) || piece.Heat == 0)
            { Dirty(uid, piece); _appearance.SetData(uid, ArtisanVisuals.Active, piece.Heat > 0); }
        }
        var query = EntityQueryEnumerator<ArtisanStationComponent>();
        while (query.MoveNext(out var uid, out var comp))
        {
            if (MetaData(uid).EntityPaused) continue;
            Entity<ArtisanStationComponent> ent = (uid, comp);
            var ready = Ready(ent);
            if (comp.Kind is ArtisanStationKind.Furnace or ArtisanStationKind.Kiln)
            {
                var target = ready ? comp.MaximumTemperature : 20;
                comp.Temperature += Math.Clamp(target - comp.Temperature, -comp.HeatingRate * frameTime, comp.HeatingRate * frameTime);
            }
            if (comp.Busy && ready)
            {
                if (!KeepWorking(ent)) Cancel(ent);
                else
                {
                    comp.Remaining = Math.Max(0, comp.Remaining - frameTime);
                    if (comp.Remaining == 0) Complete(ent);
                }
            }
            comp.UiCountdown -= frameTime;
            if (comp.UiCountdown <= 0)
            {
                comp.UiCountdown = 0.5f;
                UpdateOperation(ent);
                _appearance.SetData(uid, ArtisanVisuals.Active, ready && (comp.Busy || (comp.Temperature > 100 && comp.Kind is ArtisanStationKind.Furnace or ArtisanStationKind.Kiln)));
                UpdateUi(ent);
            }
        }
    }
}

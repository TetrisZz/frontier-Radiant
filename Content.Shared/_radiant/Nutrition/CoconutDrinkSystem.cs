using Content.Shared.ActionBlocker;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Kitchen.Components;
using Content.Shared.Nutrition.Components;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Tools.Systems;
using Robust.Shared.Network;

namespace Content.Shared._radiant.Nutrition;

public sealed class CoconutDrinkSystem : EntitySystem
{
    private const string ScrewQuality = "Screwing";
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedToolSystem _tools = default!;
    [Dependency] private readonly OpenableSystem _openable = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly ActionBlockerSystem _blocker = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly INetManager _net = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<CoconutDrinkComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<CoconutDrinkComponent, InteractUsingEvent>(OnInteractUsing);
        SubscribeLocalEvent<CoconutDrinkComponent, CoconutPrepareDoAfterEvent>(OnPrepared);
        SubscribeLocalEvent<CoconutDrinkComponent, OpenableOpenAttemptEvent>(OnOpenAttempt);
    }

    private void OnMapInit(Entity<CoconutDrinkComponent> ent, ref MapInitEvent args) => UpdateAppearance(ent);

    private void OnOpenAttempt(Entity<CoconutDrinkComponent> ent, ref OpenableOpenAttemptEvent args)
    {
        if (ent.Comp.State == CoconutDrinkState.Closed)
            args.Cancelled = true;
    }

    private void OnInteractUsing(Entity<CoconutDrinkComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || Preparation(ent, args.Used) == null || !_blocker.CanComplexInteract(args.User))
            return;
        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User,
            ent.Comp.State == CoconutDrinkState.Closed ? 2 : 1,
            new CoconutPrepareDoAfterEvent(), ent, ent, args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            BreakOnDropItem = true,
            NeedHand = true,
        });
    }

    private void OnPrepared(Entity<CoconutDrinkComponent> ent, ref CoconutPrepareDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Used is not { } tool || Deleted(tool) ||
            !_blocker.CanInteract(args.User, ent) || !_interaction.InRangeUnobstructed(args.User, ent.Owner) ||
            Preparation(ent, tool) is not { } state)
            return;

        var consumePaper = ent.Comp.State != CoconutDrinkState.Closed;
        ent.Comp.State = state;
        Dirty(ent);
        UpdateAppearance(ent);
        if (consumePaper && _net.IsServer)
            QueueDel(tool);
        args.Handled = true;
    }

    private CoconutDrinkState? Preparation(Entity<CoconutDrinkComponent> ent, EntityUid tool)
    {
        if (ent.Comp.State == CoconutDrinkState.Closed)
        {
            if (_tools.HasQuality(tool, ScrewQuality))
                return CoconutDrinkState.Carved;
            if (HasComp<SharpComponent>(tool))
                return CoconutDrinkState.Chopped;
        }
        else if (MetaData(tool).EntityPrototype?.ID == "Paper")
        {
            if (ent.Comp.State == CoconutDrinkState.Carved)
                return CoconutDrinkState.CarvedStraw;
            if (ent.Comp.State == CoconutDrinkState.Chopped)
                return CoconutDrinkState.ChoppedStraw;
        }
        return null;
    }

    private void UpdateAppearance(Entity<CoconutDrinkComponent> ent)
    {
        // Keep the same solution container when opening or decorating the coconut.
        _openable.SetOpen(ent, ent.Comp.State != CoconutDrinkState.Closed);
        _appearance.SetData(ent, CoconutDrinkVisuals.State, ent.Comp.State);
    }
}

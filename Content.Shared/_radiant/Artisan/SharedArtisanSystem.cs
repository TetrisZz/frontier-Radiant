using Content.Shared.Item;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;

namespace Content.Shared._radiant.Artisan;

public abstract class SharedArtisanSystem : EntitySystem
{
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SolutionTransferSystem _transfer = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ArtisanWorkpieceComponent, GettingPickedUpAttemptEvent>((uid, comp, args) =>
        { if (comp.Heat > 0) args.Cancel(); });
        SubscribeLocalEvent<ArtisanStationComponent, InteractUsingEvent>(OnInteractUsing);
    }

    protected virtual void OnInteractUsing(Entity<ArtisanStationComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || ent.Comp.Kind != ArtisanStationKind.Fermenter ||
            !TryComp<SolutionTransferComponent>(args.Used, out var transfer)) return;
        if (ent.Comp.LiquidLocked)
        {
            args.Handled = true;
            _popup.PopupClient(Loc.GetString("artisan-busy"), ent, args.User);
            return;
        }
        if (!ent.Comp.DrainMode) return;
        args.Handled = true;
        if (!transfer.CanReceive || !_solutions.TryGetRefillableSolution(args.Used, out var receiver, out _) ||
            !_solutions.TryGetDrainableSolution(ent.Owner, out var source, out _))
        {
            _popup.PopupClient(Loc.GetString("artisan-tap-needs-container"), ent, args.User);
            return;
        }
        var amount = transfer.TransferAmount;
        if (TryComp<RefillableSolutionComponent>(args.Used, out var refill) && refill.MaxRefill is { } maximum)
            amount = FixedPoint2.Min(amount, maximum);
        var moved = _transfer.Transfer(args.User, ent, source.Value, args.Used, receiver.Value, amount);
        if (moved > 0)
            _popup.PopupClient(Loc.GetString("artisan-tap-transferred", ("amount", moved)), ent, args.User);
    }
}

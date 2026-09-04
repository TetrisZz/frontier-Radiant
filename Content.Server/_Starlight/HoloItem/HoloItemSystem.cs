using Content.Server.PowerCell;
using Content.Shared.Interaction;
using Robust.Shared.Spawners;

namespace Content.Server._Starlight.HoloItem;

// Radiant sector: functional holographic restraint projector.
public sealed class HoloItemSystem : EntitySystem
{
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly IComponentFactory _components = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HoloItemComponent, AfterInteractEvent>(OnInteract);
    }

    private void OnInteract(EntityUid uid, HoloItemComponent comp, AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target)
            return;

        foreach (var name in comp.RequiredComponents)
        {
            if (!_components.TryGetRegistration(name, out var registration) || !EntityManager.HasComponent(target, registration.Type))
                return;
        }

        if (!_powerCell.TryUseCharge(uid, comp.ChargeUse, user: args.User))
            return;

        var holo = SpawnAtPosition(comp.ItemPrototype, Transform(uid).Coordinates);
        EnsureComp<TimedDespawnComponent>(holo);
        var interact = new AfterInteractEvent(args.User, holo, target, args.ClickLocation, true);
        RaiseLocalEvent(holo, interact);
        args.Handled = true;
    }
}

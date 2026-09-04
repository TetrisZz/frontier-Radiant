using Content.Shared._Starlight.Clothing.Components;
using Content.Shared._Starlight.Movement.Components;
using Content.Shared.Body.Components;
using Content.Shared.Inventory;
using Content.Shared.Movement.Systems;

namespace Content.Shared._Starlight.Movement;

public sealed class MovementHinderedByShoesSystem : EntitySystem
{
    [Dependency] private readonly InventorySystem _inventory = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<BodyComponent, RefreshMovementSpeedModifiersEvent>(OnRefreshSpeed);
    }

    private void OnRefreshSpeed(EntityUid uid, BodyComponent body, ref RefreshMovementSpeedModifiersEvent args)
    {
        if (!_inventory.TryGetSlotEntity(uid, "shoes", out var shoes))
            return;

        var hinderModifier = 0f;
        foreach (var leg in body.LegEntities)
        {
            if (TryComp<MovementBodyPartHinderedByShoesComponent>(leg, out var legModifier))
                hinderModifier += legModifier.HinderModifier;
        }

        if (TryComp<OverrideShoesHinderComponent>(shoes, out var overrideComponent))
            hinderModifier *= overrideComponent.HinderModifier;

        if (hinderModifier > 0f)
            args.ModifySpeed(1f, 1f - hinderModifier);
    }
}

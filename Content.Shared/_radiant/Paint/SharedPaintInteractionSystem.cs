using Content.Shared.Interaction;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Storage.EntitySystems;

namespace Content.Shared._radiant.Paint;

public sealed class SharedPaintInteractionSystem : EntitySystem
{
    public override void Initialize()
    {
        SubscribeLocalEvent<ItemComponent, InteractUsingEvent>(OnInteractUsing,
            before: new[] { typeof(SharedStorageSystem) });
    }

    private void OnInteractUsing(Entity<ItemComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || args.Used == ent.Owner || HasComp<MobStateComponent>(ent) ||
            !HasComp<PaintCanComponent>(args.Used))
            return;

        // Consume the interaction on both sides before a bag can insert the can.
        args.Handled = true;
        var attempt = new PaintItemAttemptEvent(args.User, ent.Owner);
        RaiseLocalEvent(args.Used, ref attempt);
    }
}

[ByRefEvent]
public readonly record struct PaintItemAttemptEvent(EntityUid User, EntityUid Target);

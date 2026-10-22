using Content.Shared._radiant.Fishing.Components;
using Content.Shared.Interaction;
using Content.Shared.Storage;
using Content.Shared.Storage.EntitySystems;

namespace Content.Shared._radiant.Decorations;

public sealed class AquariumInteractionSystem : EntitySystem
{
    [Dependency] private readonly SharedStorageSystem _storage = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<AquariumComponent, StorageInteractUsingAttemptEvent>(OnStorageInteractUsingAttempt);
        SubscribeLocalEvent<AquariumComponent, AfterInteractUsingEvent>(OnAfterInteractUsing);
    }

    private void OnStorageInteractUsingAttempt(Entity<AquariumComponent> ent, ref StorageInteractUsingAttemptEvent args)
    {
        // Let the held item pour water or dispense food before trying to store it.
        args.Cancelled = true;
    }

    private void OnAfterInteractUsing(EntityUid uid, AquariumComponent component, AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach ||
            (!HasComp<FishComponent>(args.Used) && !HasComp<AquariumDecorationComponent>(args.Used)) ||
            !TryComp<StorageComponent>(uid, out var storage))
            return;

        var attempt = new StorageInteractAttemptEvent(false);
        RaiseLocalEvent(uid, ref attempt);
        if (attempt.Cancelled)
            return;

        args.Handled = true;
        _storage.PlayerInsertHeldEntity((uid, storage), args.User);
    }
}

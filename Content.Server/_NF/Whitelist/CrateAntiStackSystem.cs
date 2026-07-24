using Content.Shared._NF.Whitelist.Components;
using Content.Shared.Physics;
using Content.Shared.Storage.Components;
using Robust.Shared.Physics.Systems;

namespace Content.Server._NF.Whitelist;

/// <summary>
/// Radiant Sector: prevents a crate that overlaps another closed crate from being closed.
/// </summary>
public sealed class CrateAntiStackSystem : EntitySystem
{
    [Dependency] private readonly SharedPhysicsSystem _physics = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<NFCrateComponent, StorageCloseAttemptEvent>(OnCloseAttempt);
    }

    private void OnCloseAttempt(Entity<NFCrateComponent> ent, ref StorageCloseAttemptEvent args)
    {
        foreach (var other in _physics.GetEntitiesIntersectingBody(ent.Owner, (int) CollisionGroup.AllMask))
        {
            if (other == ent.Owner ||
                !HasComp<NFCrateComponent>(other) ||
                !TryComp(other, out EntityStorageComponent? storage) ||
                storage.Open)
            {
                continue;
            }

            args.Cancelled = true;
            return;
        }
    }
}

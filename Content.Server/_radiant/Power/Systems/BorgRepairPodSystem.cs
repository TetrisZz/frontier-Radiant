using Content.Server._radiant.Power.Components;
using Content.Server.Power.Components;
using Content.Shared.Damage;
using Content.Shared.Emp;
using Robust.Server.Containers;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Power.Systems;

public sealed class BorgRepairPodSystem : EntitySystem
{
    private const string StorageContainer = "entity_storage";

    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BorgRepairPodComponent, ApcPowerReceiverComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var repair, out var power, out var transform))
        {
            if (_timing.CurTime < repair.NextRepairTime)
                continue;

            repair.NextRepairTime = _timing.CurTime + TimeSpan.FromSeconds(repair.RepairInterval);

            if (!transform.Anchored || !power.Powered || HasComp<EmpDisabledComponent>(uid))
                continue;

            if (!_container.TryGetContainer(uid, StorageContainer, out var storage))
                continue;

            foreach (var contained in storage.ContainedEntities)
                _damageable.TryChangeDamage(contained, repair.Damage, true, origin: uid);
        }
    }
}

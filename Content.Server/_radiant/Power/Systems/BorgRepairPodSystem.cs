using Content.Server._radiant.Power.Components;
using Content.Server.Power.Components;
using Content.Shared.Construction.Components;
using Content.Shared.Damage;
using Content.Shared.Emp;
using Robust.Server.Containers;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Power.Systems;

public sealed class BorgRepairPodSystem : EntitySystem
{
    private const string StorageContainer = "entity_storage";

    [Dependency] private readonly ContainerSystem _container = default!;
    [Dependency] private readonly DamageableSystem _damageable = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
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

            var repaired = false;
            foreach (var contained in storage.ContainedEntities)
            {
                if (_damageable.TryChangeDamage(contained, repair.Damage, true, origin: uid) is { Empty: false })
                    repaired = true;
            }

            if (repaired)
                _audio.PlayPvs(repair.RepairSound, uid);
        }
    }

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<BorgRepairPodComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BorgRepairPodComponent, RefreshPartsEvent>(OnRefreshParts);
        SubscribeLocalEvent<BorgRepairPodComponent, UpgradeExamineEvent>(OnUpgradeExamine);
    }

    private void OnStartup(Entity<BorgRepairPodComponent> entity, ref ComponentStartup args)
    {
        entity.Comp.BaseDamage = entity.Comp.Damage;

        if (TryComp<ChargerComponent>(entity, out var charger))
            entity.Comp.BaseChargeRate = charger.ChargeRate;
    }

    private void OnRefreshParts(Entity<BorgRepairPodComponent> entity, ref RefreshPartsEvent args)
    {
        if (!TryComp<ChargerComponent>(entity, out var charger))
            return;

        entity.Comp.RepairMultiplier = GetUpgradeMultiplier(args.PartRatings["Manipulator"]);
        entity.Comp.ChargeMultiplier = GetUpgradeMultiplier(args.PartRatings["Capacitor"]);
        entity.Comp.Damage = entity.Comp.BaseDamage * entity.Comp.RepairMultiplier;
        charger.ChargeRate = entity.Comp.BaseChargeRate * entity.Comp.ChargeMultiplier;
    }

    private void OnUpgradeExamine(Entity<BorgRepairPodComponent> entity, ref UpgradeExamineEvent args)
    {
        // Tier 1 is the baseline set of parts, not an upgrade.
        if (entity.Comp.RepairMultiplier > 1f)
            args.AddPercentageUpgrade("borg-repair-pod-upgrade-repair", entity.Comp.RepairMultiplier);
        if (entity.Comp.ChargeMultiplier > 1f)
            args.AddPercentageUpgrade("borg-repair-pod-upgrade-charge", entity.Comp.ChargeMultiplier);
    }

    /// <summary>
    /// Radiant Sector: the pod has deliberately fixed, non-exponential upgrade steps.
    /// </summary>
    private static float GetUpgradeMultiplier(float rating)
    {
        return rating switch
        {
            <= 1 => 1f,
            2 => 1.4f,
            3 => 1.6f,
            4 => 2f,
            _ => 3f,
        };
    }
}

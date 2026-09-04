using Content.Server.Emp;
using Content.Server.PowerCell;
using Content.Shared.Item.ItemToggle;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Map;

namespace Content.Server._Starlight.Emp;

// Radiant sector: activates an EMP pulse when the powered cyber fist lands a hit.
public sealed class EmpOnMeleeHitSystem : EntitySystem
{
    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly ItemToggleSystem _toggle = default!;
    [Dependency] private readonly PowerCellSystem _powerCell = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<EmpOnMeleeHitComponent, MeleeHitEvent>(OnHit);
    }

    private void OnHit(EntityUid uid, EmpOnMeleeHitComponent comp, MeleeHitEvent args)
    {
        if (args.HitEntities.Count == 0 || !_toggle.IsActivated(uid) || !_powerCell.TryUseActivatableCharge(uid))
            return;

        if (comp.DisableOnHit)
            _toggle.TryDeactivate(uid);

        foreach (var target in args.HitEntities)
            _emp.EmpPulse(_transform.GetMapCoordinates(target), comp.Range, comp.EnergyConsumption, (float) comp.DisableDuration.TotalSeconds);
    }
}

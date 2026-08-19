using Content.Server.Audio;
using Content.Server.Emp;
using Content.Server.Power.Components;
using Content.Server.Power.EntitySystems;
using Content.Server.Station.Systems;
using Content.Shared._NF.BindToStation;
using Content.Shared._NF.EmpGenerator;
using Robust.Server.Audio;
using Robust.Server.GameObjects;
using Robust.Shared.Timing;

namespace Content.Server._NF.EmpGenerator;

public sealed class EmpGeneratorSystem : EntitySystem
{
    // Radiant Sector: plays the EMP grenade priming sound when the generator starts its countdown.
    [Dependency] private readonly AudioSystem _audio = default!;
    [Dependency] private readonly SharedPointLightSystem _lights = default!;
    [Dependency] private readonly EmpSystem _emp = default!;
    [Dependency] private readonly TransformSystem _transform = default!;
    [Dependency] private readonly StationSystem _station = default!;
    // Radiant Sector: schedules the delayed EMP pulse.
    [Dependency] private readonly IGameTiming _timing = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<EmpGeneratorComponent, PowerChargeActionEvent>(OnAction);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var query = EntityQueryEnumerator<EmpGeneratorComponent, PowerChargeComponent>();
        while (query.MoveNext(out var uid, out var grav, out var charge))
        {
            if (!_lights.TryGetLight(uid, out var pointLight))
                continue;

            _lights.SetEnabled(uid, charge.Charge > 0, pointLight);
            _lights.SetRadius(uid, MathHelper.Lerp(grav.LightRadiusMin, grav.LightRadiusMax, charge.Charge),
                pointLight);
        }

        // Radiant Sector: fire each queued EMP pulse only after its warning delay.
        var pendingQuery = EntityQueryEnumerator<EmpGeneratorComponent>();
        while (pendingQuery.MoveNext(out var uid, out var generator))
        {
            if (generator.PendingPulseAt is not { } pulseAt || pulseAt > _timing.CurTime)
                continue;

            generator.PendingPulseAt = null;
            Dirty(uid, generator);
            TriggerPulse((uid, generator));
        }
    }

    private void OnAction(Entity<EmpGeneratorComponent> ent, ref PowerChargeActionEvent args)
    {
        if (TryComp<StationBoundObjectComponent>(ent, out var stationBound)
            && _station.GetOwningStation(ent) != stationBound.BoundStation)
            return;

        // Radiant Sector: warn nearby players now, then deliver the EMP after 3.5 seconds.
        if (ent.Comp.PendingPulseAt != null)
            return;

        _audio.PlayPvs(ent.Comp.ActivationSound, ent);
        ent.Comp.PendingPulseAt = _timing.CurTime + ent.Comp.ActivationDelay;
        Dirty(ent);
    }

    // Radiant Sector: executes the delayed EMP pulse while preserving the generator's grid immunity.
    private void TriggerPulse(Entity<EmpGeneratorComponent> ent)
    {
        if (!TryComp(ent, out TransformComponent? xform))
            return;

        List<EntityUid>? immuneGridList = null;
        if (xform.GridUid != null)
            immuneGridList = [xform.GridUid.Value];

        _emp.EmpPulse(_transform.ToMapCoordinates(xform.Coordinates), ent.Comp.Range, ent.Comp.EnergyConsumption,
            ent.Comp.DisableDuration, immuneGrids: immuneGridList);
    }
}

using Content.Server.Atmos.EntitySystems;
using Content.Shared._radiant.Social;
using Content.Shared.Atmos;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Popups;
using Content.Shared.Stacks;

namespace Content.Server._radiant.Social;

public sealed class SaunaHeaterSystem : EntitySystem
{
    [Dependency] private readonly SharedStackSystem _stacks = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly AtmosphereSystem _atmosphere = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SaunaHeaterComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<SaunaHeaterComponent, InteractUsingEvent>(OnFuel);
        SubscribeLocalEvent<SaunaHeaterComponent, ExaminedEvent>((Entity<SaunaHeaterComponent> ent, ref ExaminedEvent args) =>
            args.PushText(Loc.GetString("sauna-fuel", ("seconds", (int)ent.Comp.FuelSeconds))));
    }

    private void OnActivate(Entity<SaunaHeaterComponent> ent, ref ActivateInWorldEvent args)
    {
        if (args.Handled)
            return;
        args.Handled = true;
        if (ent.Comp.FuelSeconds <= 0)
        {
            _popup.PopupEntity(Loc.GetString("sauna-no-fuel"), ent, args.User);
            return;
        }
        ent.Comp.Lit = !ent.Comp.Lit;
        _appearance.SetData(ent, SaunaVisuals.Lit, ent.Comp.Lit);
    }

    private void OnFuel(Entity<SaunaHeaterComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !TryComp<StackComponent>(args.Used, out var stack) || stack.StackTypeId != "WoodPlank")
            return;
        args.Handled = true;
        var count = Math.Min(_stacks.GetCount(args.Used, stack), (int)((ent.Comp.MaximumFuelSeconds - ent.Comp.FuelSeconds) / 60));
        if (count > 0 && _stacks.Use(args.Used, count, stack))
            ent.Comp.FuelSeconds += count * 60;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<SaunaHeaterComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var heater, out var xform))
        {
            if (!heater.Lit)
                continue;
            heater.Accumulator += frameTime;
            if (heater.Accumulator < 1)
                continue;
            var elapsed = heater.Accumulator;
            heater.Accumulator = 0;
            heater.FuelSeconds = Math.Max(0, heater.FuelSeconds - elapsed);
            if (heater.FuelSeconds <= 0)
            {
                heater.Lit = false;
                _appearance.SetData(uid, SaunaVisuals.Lit, false);
                continue;
            }
            var mixture = _atmosphere.GetTileMixture((uid, xform), true);
            if (mixture == null || mixture.TotalMoles < 1)
                continue;
            if (mixture.Temperature < heater.TargetTemperature)
                mixture.Temperature = Math.Min(heater.TargetTemperature, mixture.Temperature + 2 * elapsed);
            if (!_solutions.TryGetSolution(uid, "water", out var solutionEntity, out var water) ||
                water.GetTotalPrototypeQuantity("Water") < 1)
                continue;
            _solutions.RemoveReagent(solutionEntity.Value, "Water", FixedPoint2.New(1));
            if (mixture.Pressure < Atmospherics.OneAtmosphere * 1.2f)
                mixture.AdjustMoles(Gas.WaterVapor, 0.1f);
            Spawn("SaunaSteam", xform.Coordinates);
        }
    }
}

using Content.Server.Botany.Components;
using Content.Shared._radiant.Decorations;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Destructible;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Decorations;

public sealed class ApiarySystem : EntitySystem
{
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<BeehiveComponent, InteractUsingEvent>(OnInsertBees);
        SubscribeLocalEvent<BeehiveComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<BeehiveComponent, GetVerbsEvent<AlternativeVerb>>(OnRecoverColony);
        SubscribeLocalEvent<BeehiveComponent, DestructionEventArgs>(OnDestroyed);
        SubscribeLocalEvent<HoneyFrameComponent, SolutionContainerChangedEvent>(OnHoneyChanged);
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<BeehiveComponent, TransformComponent>();
        while (query.MoveNext(out var uid, out var hive, out var xform))
        {
            if (_timing.CurTime < hive.NextProduction)
                continue;
            hive.NextProduction = _timing.CurTime + hive.Interval;
            if (hive.Occupied && xform.Anchored && !_containers.IsEntityOrParentInContainer(uid))
                ProduceHoney((uid, hive));
        }
    }

    public int CountPlants(Entity<BeehiveComponent> ent)
    {
        var count = 0;
        var coords = Transform(ent).Coordinates;
        foreach (var (uid, plant) in _lookup.GetEntitiesInRange<PlantHolderComponent>(coords, ent.Comp.PlantRange))
        {
            if (plant.Seed != null && !plant.Dead && plant.Health > 0 && plant.Age >= plant.Seed.Maturation &&
                !_containers.IsEntityOrParentInContainer(uid) &&
                _interaction.InRangeUnobstructed(ent.Owner, uid, range: ent.Comp.PlantRange))
                count++;
        }
        return count;
    }

    public void ProduceHoney(Entity<BeehiveComponent> ent)
    {
        if (!ent.Comp.Occupied || !Transform(ent).Anchored || _containers.IsEntityOrParentInContainer(ent))
            return;
        var plants = Math.Min(3, CountPlants(ent));
        if (plants == 0)
            return;
        foreach (var container in _containers.GetAllContainers(ent))
        foreach (var frame in container.ContainedEntities)
        {
            if (!HasComp<HoneyFrameComponent>(frame) || !_solutions.TryGetSolution(frame, "honey", out var solution))
                continue;
            _solutions.TryAddReagent(solution.Value, "Honey", FixedPoint2.New(ent.Comp.ProductionAmount * plants));
        }
    }

    private void OnInsertBees(Entity<BeehiveComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<BeeColonyComponent>(args.Used))
            return;
        args.Handled = true;
        if (ent.Comp.Occupied)
        {
            _popup.PopupEntity(Loc.GetString("apiary-already-occupied"), ent, args.User);
            return;
        }
        ent.Comp.Occupied = true;
        ent.Comp.NextProduction = _timing.CurTime + ent.Comp.Interval;
        QueueDel(args.Used);
        _popup.PopupEntity(Loc.GetString("apiary-settled"), ent, args.User);
    }

    private void OnExamined(Entity<BeehiveComponent> ent, ref ExaminedEvent args)
    {
        args.PushText(Loc.GetString(ent.Comp.Occupied ? "apiary-occupied" : "apiary-empty"));
        if (ent.Comp.Occupied)
            args.PushText(Loc.GetString("apiary-plants", ("count", CountPlants(ent))));
    }

    private void OnRecoverColony(Entity<BeehiveComponent> ent, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !ent.Comp.Occupied)
            return;
        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("apiary-recover-colony"),
            Act = () =>
            {
                if (!ent.Comp.Occupied || !_interaction.InRangeUnobstructed(user, ent.Owner))
                    return;
                ent.Comp.Occupied = false;
                Spawn("BeeSeeds", Transform(ent).Coordinates);
            },
        });
    }

    private void OnDestroyed(Entity<BeehiveComponent> ent, ref DestructionEventArgs args)
    {
        if (!ent.Comp.Occupied)
            return;
        ent.Comp.Occupied = false;
        for (var i = 0; i < 3; i++)
            Spawn("MobAngryBeeTimed", Transform(ent).Coordinates);
    }

    private void OnHoneyChanged(Entity<HoneyFrameComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId == "honey")
            _appearance.SetData(ent, HoneyFrameVisuals.Filled, args.Solution.Volume > FixedPoint2.Zero);
    }
}

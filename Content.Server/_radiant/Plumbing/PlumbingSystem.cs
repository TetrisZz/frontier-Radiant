using Content.Server.Fluids.EntitySystems;
using Content.Server.NodeContainer.EntitySystems;
using Content.Server._radiant.Paint;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Fluids.Components;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Verbs;

namespace Content.Server._radiant.Plumbing;

public sealed class PlumbingSystem : EntitySystem
{
    private const string Supply = "water";
    private const string Waste = "waste";

    [Dependency] private readonly NodeContainerSystem _nodes = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SolutionTransferSystem _transfers = default!;
    [Dependency] private readonly OpenableSystem _openable = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly HygieneSystem _hygiene = default!;
    [Dependency] private readonly PuddleSystem _puddles = default!;

    private float _leakTimer;

    public override void Initialize()
    {
        SubscribeLocalEvent<PlumbedFixtureComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<PlumbedFixtureComponent, InteractUsingEvent>(OnFaucet);
        SubscribeLocalEvent<PlumbedFixtureComponent, GetVerbsEvent<Verb>>(OnFixtureVerbs);
        SubscribeLocalEvent<PaintWashStationComponent, GetVerbsEvent<Verb>>(OnSinkVerbs);
        SubscribeLocalEvent<PlumbedFixtureComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<LeakingPipeComponent, ExaminedEvent>(OnPipeExamined);
        SubscribeLocalEvent<PlumbingTankComponent, GetVerbsEvent<Verb>>(OnTankVerbs);
    }

    public override void Update(float frameTime)
    {
        _leakTimer += frameTime;
        if (_leakTimer < 2)
            return;

        _leakTimer %= 2;
        var query = EntityQueryEnumerator<LeakingPipeComponent, DamageableComponent>();
        while (query.MoveNext(out var uid, out var pipe, out var damage))
        {
            if (damage.TotalDamage < 15)
                continue;

            foreach (var tank in Tanks(uid, pipe.Port))
            {
                var volume = FixedPoint2.Min(2, tank.Comp.Solution.Volume);
                if (volume <= 0)
                    continue;

                var spilled = _solutions.SplitSolution(tank, volume);
                if (!_puddles.TrySpillAt(Transform(uid).Coordinates, spilled, out _, sound: false))
                    _solutions.AddSolution(tank, spilled);
                break;
            }
        }
    }

    private void OnPipeExamined(Entity<LeakingPipeComponent> ent, ref ExaminedEvent args)
    {
        if (args.IsInDetailsRange && TryComp<DamageableComponent>(ent, out var damage) && damage.TotalDamage >= 15)
            args.PushText(Loc.GetString("plumbing-pipe-leaking"));
    }

    private void OnMapInit(Entity<PlumbedFixtureComponent> ent, ref MapInitEvent args)
    {
        if (ent.Comp.Flush)
            RemComp<SolutionRegenerationComponent>(ent);
    }

    private IEnumerable<Entity<SolutionComponent>> Tanks(EntityUid fixture, string port)
    {
        if (!_nodes.TryGetNode<LiquidPipeNode>(fixture, port, out var node) ||
            !node.Connectable(EntityManager) || node.NodeGroup is not { Remaking: false } group)
            yield break;

        foreach (var connection in group.Nodes)
        {
            if (!connection.Connectable(EntityManager) || !HasComp<PlumbingTankComponent>(connection.Owner) ||
                !_solutions.TryGetSolution(connection.Owner, "tank", out var tank, out _))
                continue;

            yield return tank.Value;
        }
    }

    public FixedPoint2 DrawWater(EntityUid fixture, FixedPoint2 requested)
    {
        var drawn = FixedPoint2.Zero;
        foreach (var tank in Tanks(fixture, Supply))
        {
            var solution = tank.Comp.Solution;
            var water = solution.GetTotalPrototypeQuantity("Water");
            if (water <= 0 || water != solution.Volume)
                continue;

            drawn += _solutions.RemoveReagent(tank, "Water", FixedPoint2.Min(requested - drawn, water));
            if (drawn >= requested)
                break;
        }

        return drawn;
    }

    public FixedPoint2 AvailableWater(EntityUid fixture)
    {
        var water = FixedPoint2.Zero;
        foreach (var tank in Tanks(fixture, Supply))
        {
            var solution = tank.Comp.Solution;
            if (solution.GetTotalPrototypeQuantity("Water") == solution.Volume)
                water += solution.Volume;
        }

        return water;
    }

    public FixedPoint2 WasteCapacity(EntityUid fixture)
    {
        if (TryComp<PlumbedFixtureComponent>(fixture, out var data) && data.Clogged)
            return FixedPoint2.Zero;

        var capacity = FixedPoint2.Zero;
        foreach (var tank in Tanks(fixture, Waste))
            capacity += tank.Comp.Solution.AvailableVolume;
        return capacity;
    }

    public FixedPoint2 SendWaste(EntityUid fixture, Entity<SolutionComponent> source, FixedPoint2 requested)
    {
        TryComp<PlumbedFixtureComponent>(fixture, out var data);
        if (data is { Clogged: true })
            return FixedPoint2.Zero;

        var transferred = FixedPoint2.Zero;
        var buildup = FixedPoint2.Zero;
        foreach (var tank in Tanks(fixture, Waste))
        {
            var amount = FixedPoint2.Min(requested - transferred, source.Comp.Solution.Volume, tank.Comp.Solution.AvailableVolume);
            if (amount <= 0)
                continue;

            var part = _solutions.SplitSolution(source, amount);
            buildup += part.Volume - part.GetTotalPrototypeQuantity("Water");
            _solutions.AddSolution(tank, part);
            transferred += amount;
            if (transferred >= requested)
                break;
        }

        if (data != null)
            data.WasteBuildup += (float) buildup;

        return transferred;
    }

    private void OnFaucet(Entity<PlumbedFixtureComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !ent.Comp.Faucet ||
            !TryComp<SolutionTransferComponent>(args.Used, out var transfer) || !transfer.CanReceive ||
            !_solutions.TryGetRefillableSolution(args.Used, out var destination, out var solution))
            return;

        args.Handled = true;
        if (_openable.IsClosed(args.Used, args.User))
            return;

        var maximum = Comp<RefillableSolutionComponent>(args.Used).MaxRefill ?? transfer.TransferAmount;
        var requested = FixedPoint2.Min(maximum, transfer.TransferAmount, solution.AvailableVolume);
        if (requested <= 0)
            return;

        var amount = DrawWater(ent, requested);
        if (amount <= 0)
        {
            _popup.PopupEntity(Loc.GetString("plumbing-no-water"), ent, args.User);
            return;
        }

        _solutions.AddSolution(destination.Value, new Solution("Water", amount));
        _popup.PopupEntity(Loc.GetString("plumbing-faucet-filled", ("amount", amount)), ent, args.User);
    }

    private void OnFixtureVerbs(Entity<PlumbedFixtureComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        var user = args.User;
        if (ent.Comp.Flush)
        {
            args.Verbs.Add(new Verb
            {
                Text = Loc.GetString("plumbing-flush"),
                Act = () => FlushBowl(ent, user)
            });
        }

    }

    private void OnSinkVerbs(Entity<PaintWashStationComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Hands == null)
            return;

        if (TryComp<PlumbedFixtureComponent>(ent, out var fixture))
        {
            if (!fixture.Faucet)
                return;
        }
        else if (!HasComp<DrainableSolutionComponent>(ent))
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("plumbing-wash"),
            Act = () => WashAtSink(ent, user)
        });
    }

    private void OnTankVerbs(Entity<PlumbingTankComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using is not { } used ||
            !HasComp<RefillableSolutionComponent>(ent) ||
            !TryComp<SolutionTransferComponent>(used, out var transfer) || !transfer.CanReceive)
            return;

        var user = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("plumbing-extract-water"),
            Act = () =>
            {
                if (_openable.IsClosed(used, user) ||
                    !_solutions.TryGetRefillableSolution(used, out var destination, out _) ||
                    !_solutions.TryGetSolution(ent.Owner, "tank", out var source, out _))
                    return;

                var amount = Comp<RefillableSolutionComponent>(used).MaxRefill ?? transfer.TransferAmount;
                _transfers.Transfer(user, ent, source.Value, used, destination.Value, FixedPoint2.Min(amount, transfer.TransferAmount));
            }
        });
    }

    private void WashAtSink(EntityUid sink, EntityUid user)
    {
        if (!_hygiene.CanWashAtSink(user))
        {
            _popup.PopupEntity(Loc.GetString("plumbing-wash-unavailable"), sink, user);
            return;
        }

        if (TryComp<PlumbedFixtureComponent>(sink, out _))
        {
            if (!UseWater(sink, user, 5))
                return;
        }
        else
        {
            if (!_solutions.TryGetSolution(sink, "tank", out var tank, out var solution) ||
                solution.GetTotalPrototypeQuantity("Water") < 5 || solution.Volume != solution.GetTotalPrototypeQuantity("Water"))
            {
                _popup.PopupEntity(Loc.GetString("plumbing-no-water"), sink, user);
                return;
            }

            _solutions.RemoveReagent(tank.Value, "Water", 5);
        }

        _hygiene.WashAtSink(user);
        _popup.PopupEntity(Loc.GetString("plumbing-washed"), sink, user);
    }

    private void FlushBowl(EntityUid toilet, EntityUid user)
    {
        if (!_solutions.TryGetSolution(toilet, "tank", out var bowl, out var bowlSolution) ||
            !_solutions.TryGetSolution(toilet, DrainComponent.SolutionName, out var drain, out var drainSolution) ||
            !UseWater(toilet, user, 10, bowlSolution.Volume + drainSolution.Volume))
            return;

        SendWaste(toilet, bowl.Value, bowlSolution.Volume);
        SendWaste(toilet, drain.Value, drainSolution.Volume);
        _popup.PopupEntity(Loc.GetString("plumbing-flushed"), toilet, user);
    }

    private bool UseWater(EntityUid fixture, EntityUid user, FixedPoint2 water, FixedPoint2 extraWaste = default)
    {
        if (AvailableWater(fixture) < water)
        {
            _popup.PopupEntity(Loc.GetString("plumbing-no-water"), fixture, user);
            return false;
        }

        if (WasteCapacity(fixture) < water + extraWaste)
        {
            _popup.PopupEntity(Loc.GetString("plumbing-drain-remainder"), fixture, user);
            return false;
        }

        var used = new Solution("Water", DrawWater(fixture, water));
        foreach (var tank in Tanks(fixture, Waste))
        {
            _solutions.TryTransferSolution(tank, used, used.Volume);
            if (used.Volume <= 0)
                break;
        }
        return true;
    }

    private void OnExamined(Entity<PlumbedFixtureComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange)
            return;

        if (_nodes.TryGetNode<LiquidPipeNode>(ent.Owner, Supply, out _))
            args.PushText(Loc.GetString("plumbing-examine-water", ("amount", AvailableWater(ent))));

        if (ent.Comp.Waste)
            args.PushText(Loc.GetString("plumbing-examine-waste", ("amount", WasteCapacity(ent))));
        if (ent.Comp.Clogged)
            args.PushText(Loc.GetString("plumbing-clogged"));
    }
}

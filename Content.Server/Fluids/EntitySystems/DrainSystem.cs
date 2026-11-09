using Content.Server._radiant.Plumbing;
using Content.Server.DoAfter;
using Content.Server.Popups;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Audio;
using Content.Shared.Chemistry.Components.SolutionManager;
using Content.Shared.Database;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Fluids;
using Content.Shared.Fluids.Components;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Tag;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Utility;

namespace Content.Server.Fluids.EntitySystems;

public sealed class DrainSystem : SharedDrainSystem
{
    private static readonly ProtoId<TagPrototype> GoldenPlunger = "GoldenPlunger";
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutionContainerSystem = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambientSoundSystem = default!;
    [Dependency] private readonly SharedAudioSystem _audioSystem = default!;
    [Dependency] private readonly PopupSystem _popupSystem = default!;
    [Dependency] private readonly TagSystem _tagSystem = default!;
    [Dependency] private readonly DoAfterSystem _doAfterSystem = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly IPrototypeManager _prototypeManager = default!;

    [Dependency] private readonly PlumbingSystem _plumbing = default!;
    [Dependency] private readonly OpenableSystem _openable = default!;

    private readonly HashSet<Entity<PuddleComponent>> _puddles = new();

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<DrainComponent, MapInitEvent>(OnDrainMapInit);
        SubscribeLocalEvent<DrainComponent, GetVerbsEvent<Verb>>(AddEmptyVerb);
        SubscribeLocalEvent<DrainComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<DrainComponent, AfterInteractUsingEvent>(OnInteract);
        SubscribeLocalEvent<DrainComponent, DrainDoAfterEvent>(OnDoAfter);
    }

    private void OnDrainMapInit(Entity<DrainComponent> ent, ref MapInitEvent args)
    {
        // Randomise puddle drains so roundstart ones don't all dump at the same time.
        ent.Comp.Accumulator = _random.NextFloat(ent.Comp.DrainFrequency);
    }

    private void AddEmptyVerb(Entity<DrainComponent> entity, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || args.Using == null)
            return;

        if (!HasComp<SpillableComponent>(args.Using) ||
            !TryComp(args.Target, out DrainComponent? drain))
            return;

        var used = args.Using.Value;
        var target = args.Target;
        var user = args.User;
        Verb verb = new()
        {
            Text = Loc.GetString("drain-component-empty-verb-inhand", ("object", Name(used))),
            Act = () =>
            {
                Empty(used, target, user, drain);
            },
            Impact = LogImpact.Low,
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/eject.svg.192dpi.png"))

        };
        args.Verbs.Add(verb);
    }

    private void Empty(EntityUid container, EntityUid target, EntityUid user, DrainComponent drain)
    {
        if (_openable.IsClosed(container, user))
            return;

        // Find the solution in the container that is emptied
        if (!_solutionContainerSystem.TryGetDrainableSolution(container, out var containerSoln, out var containerSolution) || containerSolution.Volume == FixedPoint2.Zero)
        {
            _popupSystem.PopupEntity(
                Loc.GetString("drain-component-empty-verb-using-is-empty-message", ("object", container)),
                container);
            return;
        }

        var original = containerSolution.Volume;
        FixedPoint2 transferred;
        if (TryComp<PlumbedFixtureComponent>(target, out var fixture) && fixture.Waste)
        {
            transferred = _plumbing.SendWaste(target, containerSoln.Value, original);
        }
        else if (_solutionContainerSystem.ResolveSolution(target, DrainComponent.SolutionName, ref drain.Solution, out var drainSolution))
        {
            transferred = FixedPoint2.Min(original, drainSolution.AvailableVolume);
            if (transferred > 0)
                _solutionContainerSystem.AddSolution(drain.Solution.Value, _solutionContainerSystem.SplitSolution(containerSoln.Value, transferred));
        }
        else
            return;

        if (transferred > 0)
        {
            _audioSystem.PlayPvs(drain.ManualDrainSound, target);
            _ambientSoundSystem.SetAmbience(target, true);
        }

        if (transferred < original)
            _popupSystem.PopupEntity(Loc.GetString("plumbing-drain-remainder"), target, user);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        var managerQuery = GetEntityQuery<SolutionContainerManagerComponent>();

        var query = EntityQueryEnumerator<DrainComponent>();
        while (query.MoveNext(out var uid, out var drain))
        {
            drain.Accumulator += frameTime;
            if (drain.Accumulator < drain.DrainFrequency)
            {
                continue;
            }
            drain.Accumulator -= drain.DrainFrequency;

            if (!managerQuery.TryGetComponent(uid, out var manager))
                continue;

            // Best to do this one every second rather than once every tick...
            if (!_solutionContainerSystem.ResolveSolution((uid, manager), DrainComponent.SolutionName, ref drain.Solution, out var drainSolution))
                continue;

            if (drainSolution.Volume <= 0 && !drain.AutoDrain)
            {
                _ambientSoundSystem.SetAmbience(uid, false);
                continue;
            }

            var plumbed = TryComp<PlumbedFixtureComponent>(uid, out var fixture) && fixture.Waste;
            if (plumbed)
                _plumbing.SendWaste(uid, drain.Solution.Value, FixedPoint2.New(drain.UnitsPerSecond * drain.DrainFrequency));
            else
                _solutionContainerSystem.SplitSolution(drain.Solution.Value, (drain.UnitsDestroyedPerSecond * drain.DrainFrequency));

            // This will ensure that UnitsPerSecond is per second...
            var amount = drain.UnitsPerSecond * drain.DrainFrequency;

            if (drain.AutoDrain)
            {
                _puddles.Clear();
                _lookup.GetEntitiesInRange(Transform(uid).Coordinates, drain.Range, _puddles);

                if (_puddles.Count == 0 || (plumbed && _plumbing.WasteCapacity(uid) <= 0))
                {
                    _ambientSoundSystem.SetAmbience(uid, false);
                    continue;
                }

                _ambientSoundSystem.SetAmbience(uid, true);

                amount /= _puddles.Count;

                foreach (var puddle in _puddles)
                {
                    if (plumbed && Transform(puddle).GridUid != Transform(uid).GridUid)
                        continue;

                    // Queue the solution deletion if it's empty. EvaporationSystem might also do this
                    // but queuedelete should be pretty safe.
                    if (!_solutionContainerSystem.ResolveSolution(puddle.Owner, puddle.Comp.SolutionName, ref puddle.Comp.Solution, out var puddleSolution))
                    {
                        QueueDel(puddle);
                        continue;
                    }

                    if (plumbed)
                    {
                        _plumbing.SendWaste(uid, puddle.Comp.Solution.Value, FixedPoint2.New(amount));
                        if (puddleSolution.Volume <= 0)
                            QueueDel(puddle);
                        continue;
                    }

                    // Removes the lowest of:
                    // the drain component's units per second adjusted for # of puddles
                    // the puddle's remaining volume (making it cleanly zero)
                    // the drain's remaining volume in its buffer.
                    var transferSolution = _solutionContainerSystem.SplitSolution(puddle.Comp.Solution.Value,
                        FixedPoint2.Min(FixedPoint2.New(amount), puddleSolution.Volume, drainSolution.AvailableVolume));

                    drainSolution.AddSolution(transferSolution, _prototypeManager);

                    if (puddleSolution.Volume <= 0)
                    {
                        QueueDel(puddle);
                    }
                }
            }

            _solutionContainerSystem.UpdateChemicals(drain.Solution.Value);
        }
    }

    private void OnExamined(Entity<DrainComponent> entity, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange ||
            !HasComp<SolutionContainerManagerComponent>(entity) ||
            !_solutionContainerSystem.ResolveSolution(entity.Owner, DrainComponent.SolutionName, ref entity.Comp.Solution, out var drainSolution))
        {
            return;
        }

        var text = drainSolution.AvailableVolume != 0
            ? Loc.GetString("drain-component-examine-volume", ("volume", drainSolution.AvailableVolume))
            : Loc.GetString("drain-component-examine-hint-full");
        args.PushMarkup(text);
    }

    private void OnInteract(Entity<DrainComponent> entity, ref AfterInteractUsingEvent args)
    {
        if (!args.CanReach || args.Target == null ||
            (!_tagSystem.HasTag(args.Used, DrainComponent.PlungerTag) && !_tagSystem.HasTag(args.Used, GoldenPlunger)))
            return;

        var clogged = TryComp<PlumbedFixtureComponent>(entity, out var fixture) && fixture.Waste
            ? fixture.Clogged
            : _solutionContainerSystem.ResolveSolution(entity.Owner, DrainComponent.SolutionName, ref entity.Comp.Solution, out var drainSolution)
              && drainSolution.AvailableVolume <= 0;
        if (!clogged)
        {
            _popupSystem.PopupEntity(Loc.GetString("drain-component-unclog-notapplicable", ("object", args.Target.Value)), args.Target.Value);
            return;
        }

        _audioSystem.PlayPvs(entity.Comp.PlungerSound, entity);


        var duration = entity.Comp.UnclogDuration * (_tagSystem.HasTag(args.Used, GoldenPlunger) ? 0.5f : 1f);
        var doAfterArgs = new DoAfterArgs(EntityManager, args.User, duration, new DrainDoAfterEvent(), entity, args.Target, args.Used)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            BreakOnHandChange = true
        };

        args.Handled = _doAfterSystem.TryStartDoAfter(doAfterArgs);
    }

    private void OnDoAfter(Entity<DrainComponent> entity, ref DrainDoAfterEvent args)
    {
        if (args.Cancelled || args.Handled || args.Target == null)
            return;

        args.Handled = true;
        if (TryComp<PlumbedFixtureComponent>(entity, out var fixture) && fixture.Waste)
        {
            fixture.WasteBuildup = 0;
            _audioSystem.PlayPvs(entity.Comp.UnclogSound, args.Target.Value);
            _popupSystem.PopupEntity(Loc.GetString("plumbing-unclogged"), args.Target.Value, args.User);
            return;
        }


        if (!_random.Prob(entity.Comp.UnclogProbability))
        {
            _popupSystem.PopupEntity(Loc.GetString("drain-component-unclog-fail", ("object", args.Target.Value)), args.Target.Value);
            return;
        }


        if (!_solutionContainerSystem.ResolveSolution(args.Target.Value, DrainComponent.SolutionName, ref entity.Comp.Solution))
        {
            return;
        }


        _solutionContainerSystem.RemoveAllSolution(entity.Comp.Solution.Value);
        _audioSystem.PlayPvs(entity.Comp.UnclogSound, args.Target.Value);
        _popupSystem.PopupEntity(Loc.GetString("drain-component-unclog-success", ("object", args.Target.Value)), args.Target.Value);
    }
}

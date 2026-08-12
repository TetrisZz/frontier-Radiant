using Content.Server.Popups;
using Content.Server.Stunnable;
using Content.Shared._radiant.Fitness;
using Content.Shared.Buckle.Components;
using Content.Shared.Coordinates;
using Content.Shared.DoAfter;
using Content.Shared.Verbs;
using Content.Shared.Weapons.Melee.Events;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Fitness;

public sealed class FitnessSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly StunSystem _stun = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private readonly SoundSpecifier _punchSound = new SoundCollectionSpecifier("BoxingHit");
    private readonly SoundSpecifier _benchStartSound = new SoundPathSpecifier("/Audio/Effects/metal_scrape1.ogg");
    private readonly SoundSpecifier _benchFinishSound = new SoundCollectionSpecifier("MetalThud");
    private readonly SoundSpecifier _bikeSound = new SoundPathSpecifier("/Audio/Items/ratchet.ogg");

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<PunchingBagComponent, AttackedEvent>(OnBagAttacked);
        SubscribeLocalEvent<BenchPressComponent, GetVerbsEvent<AlternativeVerb>>(OnBenchVerbs);
        SubscribeLocalEvent<BenchPressComponent, BenchPressDoAfterEvent>(OnBenchFinished);
        SubscribeLocalEvent<ExerciseBikeComponent, GetVerbsEvent<AlternativeVerb>>(OnBikeVerbs);
        SubscribeLocalEvent<ExerciseBikeComponent, ExerciseBikeDoAfterEvent>(OnBikeFinished);
        SubscribeLocalEvent<BuckleComponent, UnbuckledEvent>(OnBenchUnbuckled);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<PunchingBagComponent>();
        while (query.MoveNext(out var uid, out var bag))
        {
            if (bag.AnimationEnd == TimeSpan.Zero || _timing.CurTime < bag.AnimationEnd)
                continue;

            bag.AnimationEnd = TimeSpan.Zero;
            _appearance.SetData(uid, FitnessVisuals.Active, false);
        }
    }

    private void OnBagAttacked(Entity<PunchingBagComponent> entity, ref AttackedEvent args)
    {
        AnimatePunch(entity);
    }

    private void AnimatePunch(Entity<PunchingBagComponent> entity)
    {
        entity.Comp.AnimationEnd = _timing.CurTime + TimeSpan.FromSeconds(1.1);
        _appearance.SetData(entity.Owner, FitnessVisuals.Active, true);
        _audio.PlayPvs(_punchSound, entity.Owner);
    }

    private void OnBenchVerbs(Entity<BenchPressComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract ||
            !TryComp<BuckleComponent>(args.User, out var buckle) ||
            buckle.BuckledTo != entity.Owner)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("fitness-benchpress-start"),
            Act = () => StartBench(entity, user),
        });
    }

    private void OnBikeVerbs(Entity<ExerciseBikeComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract ||
            !TryComp<BuckleComponent>(args.User, out var buckle) ||
            buckle.BuckledTo != entity.Owner)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("fitness-bike-start"),
            Act = () => StartBike(entity, user),
        });
    }

    private void StartBike(Entity<ExerciseBikeComponent> entity, EntityUid user)
    {
        if (!TryComp<BuckleComponent>(user, out var buckle) || buckle.BuckledTo != entity.Owner)
            return;

        if (entity.Comp.InUse)
        {
            _popup.PopupEntity(Loc.GetString("fitness-bike-in-use"), entity.Owner, user);
            return;
        }

        entity.Comp.InUse = true;
        entity.Comp.User = user;
        _appearance.SetData(entity.Owner, FitnessVisuals.Active, true);
        _audio.PlayPvs(_bikeSound, entity.Owner);

        var doAfter = new DoAfterArgs(EntityManager,
            user,
            entity.Comp.ExerciseDuration,
            new ExerciseBikeDoAfterEvent(),
            entity.Owner,
            target: entity.Owner)
        {
            BreakOnDamage = true,
            BreakOnMove = false,
            NeedHand = false,
            RequireCanInteract = false,
            BlockDuplicate = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            FinishBike(entity);
    }

    private void StartBench(Entity<BenchPressComponent> entity, EntityUid user)
    {
        if (!TryComp<BuckleComponent>(user, out var buckle) || buckle.BuckledTo != entity.Owner)
            return;

        if (entity.Comp.InUse)
        {
            _popup.PopupEntity(Loc.GetString("fitness-benchpress-in-use"), entity.Owner, user);
            return;
        }

        entity.Comp.InUse = true;
        entity.Comp.User = user;
        _appearance.SetData(entity.Owner, FitnessVisuals.Active, true);
        entity.Comp.BarbellVisual = SpawnAttachedTo("FitnessBenchPressBarbellVisual", entity.Owner.ToCoordinates());

        var duration = TimeSpan.FromSeconds(entity.Comp.ExerciseDuration);
        _stun.TryAddStunDuration(user, duration);
        _audio.PlayPvs(_benchStartSound, entity.Owner);

        var doAfter = new DoAfterArgs(EntityManager,
            user,
            entity.Comp.ExerciseDuration,
            new BenchPressDoAfterEvent(),
            entity.Owner,
            target: entity.Owner)
        {
            BreakOnDamage = true,
            BreakOnMove = false,
            NeedHand = false,
            RequireCanInteract = false,
            BlockDuplicate = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            FinishBench(entity);
    }

    private void OnBenchUnbuckled(Entity<BuckleComponent> entity, ref UnbuckledEvent args)
    {
        if (TryComp<BenchPressComponent>(args.Strap.Owner, out var bench) && bench.User == entity.Owner)
            FinishBench((args.Strap.Owner, bench));

        if (TryComp<ExerciseBikeComponent>(args.Strap.Owner, out var bike) && bike.User == entity.Owner)
            FinishBike((args.Strap.Owner, bike));
    }

    private void OnBenchFinished(Entity<BenchPressComponent> entity, ref BenchPressDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        FinishBench(entity);

        if (!args.Cancelled)
        {
            _audio.PlayPvs(_benchFinishSound, entity.Owner);
            _popup.PopupEntity(Loc.GetString("fitness-benchpress-finished"), args.Args.User, args.Args.User);
        }
    }

    private void FinishBench(Entity<BenchPressComponent> entity)
    {
        if (entity.Comp.BarbellVisual is { } visual)
            QueueDel(visual);

        entity.Comp.InUse = false;
        entity.Comp.User = null;
        entity.Comp.BarbellVisual = null;
        _appearance.SetData(entity.Owner, FitnessVisuals.Active, false);
    }

    private void OnBikeFinished(Entity<ExerciseBikeComponent> entity, ref ExerciseBikeDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        FinishBike(entity);

        if (!args.Cancelled)
            _popup.PopupEntity(Loc.GetString("fitness-bike-finished"), args.Args.User, args.Args.User);
    }

    private void FinishBike(Entity<ExerciseBikeComponent> entity)
    {
        entity.Comp.InUse = false;
        entity.Comp.User = null;
        _appearance.SetData(entity.Owner, FitnessVisuals.Active, false);
    }
}

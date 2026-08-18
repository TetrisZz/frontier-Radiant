using Content.Server.Atmos.EntitySystems;
using Content.Server.Body.Systems;
using Content.Server.Hands.Systems;
using Content.Server.Popups;
using Content.Server.Fluids.EntitySystems;
using Content.Shared._radiant.Nutrition;
using Content.Shared.Atmos;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.DoAfter;
using Content.Shared.Interaction;
using Content.Shared.Nutrition.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Random;

namespace Content.Server._radiant.Nutrition;

public sealed class HookahSystem : EntitySystem
{
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly AtmosphereSystem _atmos = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly BloodstreamSystem _bloodstream = default!;
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly HandsSystem _hands = default!;
    [Dependency] private readonly IngestionSystem _ingestion = default!;
    [Dependency] private readonly PopupSystem _popup = default!;
    [Dependency] private readonly PuddleSystem _puddle = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ReactiveSystem _reactive = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solution = default!;
    [Dependency] private readonly SmokeSystem _smoke = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<HookahComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<HookahComponent, InteractHandEvent>(OnInteractHand);
        SubscribeLocalEvent<HookahComponent, GetVerbsEvent<AlternativeVerb>>(OnGetVerbs);
        SubscribeLocalEvent<HookahComponent, HookahDoAfterEvent>(OnDoAfter);
        SubscribeLocalEvent<HookahComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnGetVerbs(Entity<HookahComponent> entity, ref GetVerbsEvent<AlternativeVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract || entity.Comp.InUse ||
            !_solution.TryGetRefillableSolution(entity.Owner, out _, out var solution) ||
            solution.Volume <= 0)
            return;

        var user = args.User;
        args.Verbs.Add(new AlternativeVerb
        {
            Text = Loc.GetString("hookah-dump-verb"),
            Act = () => DumpContents(entity, user),
        });
    }

    private void DumpContents(Entity<HookahComponent> entity, EntityUid user)
    {
        if (entity.Comp.InUse ||
            !_solution.TryGetRefillableSolution(entity.Owner, out var solutionEntity, out var solution) ||
            solution.Volume <= 0)
            return;

        var dumped = _solution.SplitSolution(solutionEntity.Value, solution.Volume);
        _puddle.TrySpillAt(entity.Owner, dumped, out _);
        _popup.PopupEntity(Loc.GetString("hookah-dump"), entity.Owner, user);
    }

    private void OnMapInit(Entity<HookahComponent> entity, ref MapInitEvent args)
    {
        _appearance.SetData(entity.Owner, HookahVisuals.Active, false);
    }

    private void OnInteractHand(Entity<HookahComponent> entity, ref InteractHandEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;

        if (entity.Comp.InUse)
        {
            _popup.PopupEntity(Loc.GetString("hookah-in-use"), entity.Owner, args.User);
            return;
        }

        if (!_solution.TryGetRefillableSolution(entity.Owner, out _, out var solution) || solution.Volume <= 0)
        {
            _popup.PopupEntity(Loc.GetString("hookah-empty"), entity.Owner, args.User);
            return;
        }

        if (!HasComp<BloodstreamComponent>(args.User) || !_ingestion.HasMouthAvailable(args.User, args.User))
            return;

        var hose = SpawnAttachedTo("HookahHoseRS", Transform(args.User).Coordinates);
        if (!_hands.TryPickupAnyHand(args.User, hose, animate: false))
        {
            QueueDel(hose);
            return;
        }

        entity.Comp.InUse = true;
        entity.Comp.Hose = hose;
        _appearance.SetData(entity.Owner, HookahVisuals.Active, true);
        _popup.PopupEntity(Loc.GetString("hookah-start"), entity.Owner, args.User);

        var doAfter = new DoAfterArgs(
            EntityManager,
            args.User,
            entity.Comp.UseDelay,
            new HookahDoAfterEvent(),
            entity.Owner,
            target: entity.Owner)
        {
            BreakOnDamage = true,
            BreakOnMove = true,
            NeedHand = false,
            BlockDuplicate = true,
        };

        if (!_doAfter.TryStartDoAfter(doAfter))
            FinishUse(entity);
    }

    private void OnDoAfter(Entity<HookahComponent> entity, ref HookahDoAfterEvent args)
    {
        if (args.Handled)
            return;

        args.Handled = true;
        FinishUse(entity);

        if (args.Cancelled ||
            !TryComp<BloodstreamComponent>(args.Args.User, out var bloodstream) ||
            !_solution.TryGetRefillableSolution(entity.Owner, out var solutionEntity, out var solution) ||
            solution.Volume <= 0)
            return;

        var inhaled = _solution.SplitSolution(solutionEntity.Value, entity.Comp.PuffAmount);
        if (inhaled.Volume <= 0)
            return;

        _reactive.DoEntityReaction(args.Args.User, inhaled, ReactionMethod.Ingestion);
        _bloodstream.TryAddToChemicals((args.Args.User, bloodstream), inhaled);
        ReleaseVapor(args.Args.User, inhaled);
        _audio.PlayPvs(entity.Comp.InhaleSound, entity.Owner);
        _popup.PopupEntity(Loc.GetString("hookah-finish"), args.Args.User, args.Args.User);
        PlayRandomHookahEvent(entity, args.Args.User);
    }

    private void PlayRandomHookahEvent(Entity<HookahComponent> entity, EntityUid user)
    {
        if (_random.Prob(0.01f))
        {
            var smoke = SpawnAttachedTo("Smoke", Transform(user).Coordinates);
            var smokeSolution = new Solution("Water", 2);
            _smoke.StartSmoke(smoke, smokeSolution, 3f, 1);
            _popup.PopupEntity(Loc.GetString("hookah-event-smoke"), entity.Owner, user);
            return;
        }

        var message = _random.NextFloat() switch
        {
            < 0.03f => "hookah-event-coal",
            < 0.07f => "hookah-event-bubbles",
            < 0.12f => "hookah-event-hose",
            _ => null,
        };

        if (message != null)
            _popup.PopupEntity(Loc.GetString(message), entity.Owner, user);
    }

    private void FinishUse(Entity<HookahComponent> entity)
    {
        if (entity.Comp.Hose is { } hose && Exists(hose))
            QueueDel(hose);

        entity.Comp.Hose = null;
        entity.Comp.InUse = false;
        _appearance.SetData(entity.Owner, HookahVisuals.Active, false);
    }

    private void ReleaseVapor(EntityUid user, Solution inhaled)
    {
        var environment = _atmos.GetContainingMixture(user, true, true);
        if (environment == null)
            return;

        var vapor = new GasMixture(1) { Temperature = inhaled.Temperature };
        vapor.SetMoles(Gas.WaterVapor, inhaled.Volume.Value / 300f);
        _atmos.Merge(environment, vapor);
    }

    private void OnShutdown(Entity<HookahComponent> entity, ref ComponentShutdown args)
    {
        if (entity.Comp.Hose is { } hose && Exists(hose))
            QueueDel(hose);
    }
}

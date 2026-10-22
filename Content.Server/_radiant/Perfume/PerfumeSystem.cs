using Content.Server._radiant.Shower;
using Content.Server.Forensics;
using Content.Shared._radiant.Perfume;
using Content.Shared.Chemistry;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.DoAfter;
using Content.Shared.Examine;
using Content.Shared.Ghost;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Humanoid;
using Content.Shared.Interaction;
using Content.Shared.Interaction.Events;
using Content.Shared.Mobs.Systems;
using Content.Shared.Mobs.Components;
using Content.Shared.Popups;
using Content.Shared.Verbs;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Perfume;

public sealed class PerfumeSystem : EntitySystem
{
    [Dependency] private readonly SharedDoAfterSystem _doAfter = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;
    [Dependency] private readonly SharedTransformSystem _transform = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    public const float SmellRange = 2f;
    private TimeSpan _nextExpiryCheck;
    private readonly SoundSpecifier _spraySound = new SoundPathSpecifier("/Audio/Effects/spray2.ogg");

    public override void Initialize()
    {
        SubscribeLocalEvent<PerfumeComponent, UseInHandEvent>(OnUseInHand);
        SubscribeLocalEvent<PerfumeComponent, AfterInteractEvent>(OnAfterInteract);
        SubscribeLocalEvent<PerfumeComponent, ApplyPerfumeEvent>(OnApplyComplete);
        SubscribeLocalEvent<PerfumeComponent, ExaminedEvent>(OnBottleExamined);
        SubscribeLocalEvent<PerfumedComponent, ExaminedEvent>(OnPerfumedExamined);
        SubscribeLocalEvent<PerfumedComponent, GetVerbsEvent<Verb>>(OnSniffVerb);
        SubscribeLocalEvent<PerfumedComponent, InteractUsingEvent>(OnSoap);
        SubscribeLocalEvent<PerfumedComponent, WashPerfumeEvent>(OnWashComplete);
        SubscribeLocalEvent<PerfumedComponent, ReactionEntityEvent>(OnWater);
        SubscribeLocalEvent<ShowerSprayEvent>(OnShowerSpray);
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextExpiryCheck)
            return;

        _nextExpiryCheck = _timing.CurTime + TimeSpan.FromSeconds(1);
        var query = EntityQueryEnumerator<PerfumedComponent>();
        while (query.MoveNext(out var uid, out var perfume))
        {
            if (perfume.ExpiresAt <= _timing.CurTime)
                RemCompDeferred<PerfumedComponent>(uid);
        }
    }

    private void OnUseInHand(Entity<PerfumeComponent> ent, ref UseInHandEvent args)
    {
        if (!args.Handled)
            args.Handled = TryStartApplying(ent, args.User, args.User);
    }

    private void OnAfterInteract(Entity<PerfumeComponent> ent, ref AfterInteractEvent args)
    {
        if (!args.Handled && args.CanReach && args.Target is { } target)
            args.Handled = TryStartApplying(ent, args.User, target);
    }

    public bool TryStartApplying(Entity<PerfumeComponent> ent, EntityUid user, EntityUid target)
    {
        if (!CanApply(ent, user, target))
        {
            _popup.PopupEntity(Loc.GetString("perfume-invalid-target"), ent, user);
            return true;
        }

        if (ent.Comp.Uses <= 0)
        {
            _popup.PopupEntity(Loc.GetString("perfume-empty"), ent, user);
            return true;
        }

        return _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, user, ent.Comp.ApplyDelay,
            new ApplyPerfumeEvent(), ent, target: target, used: ent)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private bool CanApply(Entity<PerfumeComponent> ent, EntityUid user, EntityUid target) =>
        _hands.IsHolding(user, ent.Owner) &&
        HasComp<HumanoidAppearanceComponent>(target) && HasComp<MobStateComponent>(target) && !_mobState.IsDead(target) &&
        !HasComp<GhostComponent>(target) && _interaction.InRangeUnobstructed(user, target);

    public bool TryApply(Entity<PerfumeComponent> ent, EntityUid user, EntityUid target)
    {
        if (ent.Comp.Uses <= 0 || !CanApply(ent, user, target))
            return false;

        // Cosmetic fragrance never changes the personal identity used by scent tracking.
        var perfume = EnsureComp<PerfumedComponent>(target);
        perfume.Fragrance = ent.Comp.Fragrance;
        perfume.ExpiresAt = _timing.CurTime + ent.Comp.Duration;
        Dirty(target, perfume);
        ent.Comp.Uses--;
        Dirty(ent);
        _audio.PlayPvs(_spraySound, target);
        Spawn("PerfumeMist", _transform.GetMapCoordinates(target));
        _popup.PopupEntity(Loc.GetString("perfume-applied", ("fragrance", Loc.GetString(perfume.Fragrance))), target, user);
        return true;
    }

    private void OnApplyComplete(Entity<PerfumeComponent> ent, ref ApplyPerfumeEvent args)
    {
        if (!args.Handled && !args.Cancelled && args.Target is { } target)
            args.Handled = TryApply(ent, args.User, target);
    }

    public bool TryGetFragrance(EntityUid target, EntityUid observer, out string fragrance)
    {
        fragrance = string.Empty;
        if (!TryComp<PerfumedComponent>(target, out var perfume) || perfume.ExpiresAt <= _timing.CurTime ||
            HasComp<GhostComponent>(observer) || !HasComp<HumanoidAppearanceComponent>(observer) ||
            !_mobState.IsAlive(observer) || !_interaction.InRangeUnobstructed(observer, target, range: SmellRange))
            return false;

        fragrance = Loc.GetString(perfume.Fragrance);
        return true;
    }

    private void OnBottleExamined(Entity<PerfumeComponent> ent, ref ExaminedEvent args)
    {
        args.PushText(Loc.GetString("perfume-fragrance", ("fragrance", Loc.GetString(ent.Comp.Fragrance))));
        args.PushText(Loc.GetString("perfume-uses", ("uses", ent.Comp.Uses)));
    }

    private void OnPerfumedExamined(Entity<PerfumedComponent> ent, ref ExaminedEvent args)
    {
        if (TryGetFragrance(ent, args.Examiner, out var fragrance))
            args.PushText(Loc.GetString("perfume-smell", ("fragrance", fragrance)));
    }

    private void OnSniffVerb(Entity<PerfumedComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract || !TryGetFragrance(ent, args.User, out _))
            return;

        var observer = args.User;
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString("perfume-sniff"),
            DoContactInteraction = false,
            Act = () =>
            {
                if (TryGetFragrance(ent, observer, out var fragrance))
                    _popup.PopupEntity(Loc.GetString("perfume-smell", ("fragrance", fragrance)), ent, observer);
            },
        });
    }

    private void OnSoap(Entity<PerfumedComponent> ent, ref InteractUsingEvent args)
    {
        if (args.Handled || !HasComp<CleansForensicsComponent>(args.Used))
            return;

        args.Handled = _doAfter.TryStartDoAfter(new DoAfterArgs(EntityManager, args.User,
            TimeSpan.FromSeconds(3), new WashPerfumeEvent(), ent, target: ent, used: args.Used)
        {
            BreakOnMove = true,
            BreakOnDamage = true,
            NeedHand = true,
        });
    }

    private void OnWashComplete(Entity<PerfumedComponent> ent, ref WashPerfumeEvent args)
    {
        if (args.Handled || args.Cancelled || args.Used is not { } soap ||
            !HasComp<CleansForensicsComponent>(soap) || !_hands.IsHolding(args.User, soap) ||
            !_interaction.InRangeUnobstructed(args.User, ent.Owner))
            return;

        Wash(ent);
        args.Handled = true;
        _popup.PopupEntity(Loc.GetString("perfume-washed"), ent, args.User);
    }

    public void Wash(EntityUid target) => RemComp<PerfumedComponent>(target);

    private void OnWater(Entity<PerfumedComponent> ent, ref ReactionEntityEvent args)
    {
        if (args.Method == ReactionMethod.Touch && args.Reagent.ID is "Water" or "SpaceCleaner" &&
            args.ReagentQuantity.Quantity >= 5)
            Wash(ent);
    }

    private void OnShowerSpray(ref ShowerSprayEvent args)
    {
        if (args.Reagent != "Water" || args.Amount <= 0)
            return;

        foreach (var (uid, _) in _lookup.GetEntitiesInRange<PerfumedComponent>(args.Coordinates, 0.5f))
        {
            if (_interaction.InRangeUnobstructed(args.Source, uid, range: args.Range))
                Wash(uid);
        }
    }
}

using Content.Server._NF.CryoSleep;
using Content.Server._NF.Traits.Assorted;
using Content.Server._radiant.Perfume;
using Content.Server._radiant.Shower;
using Content.Server.Atmos.Components;
using Content.Shared.Alert;
using Content.Shared.Chemistry.Components;
using Content.Shared.Examine;
using Content.Shared.Interaction;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Movement.Components;
using Content.Shared.Weapons.Melee.Events;
using Content.Shared.Weapons.Ranged.Components;
using Content.Shared.Weapons.Ranged.Systems;
using Robust.Shared.Physics.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Plumbing;

[RegisterComponent]
public sealed partial class HygieneComponent : Component
{
    [DataField]
    public float Dirt;

    public TimeSpan NextPuddlePenalty;
    public TimeSpan ActiveUntil;
    public TimeSpan? NextOdorIncident;
    internal int? AppliedStage;
}

public sealed class HygieneSystem : EntitySystem
{
    private const int FilthyStage = 3;
    private static readonly ProtoId<AlertPrototype>[] StageAlerts =
        ["RadiantHygiene", "RadiantHygieneSweaty", "RadiantHygieneDirty", "RadiantHygieneFilthy"];

    [Dependency] private readonly AlertsSystem _alerts = default!;
    [Dependency] private readonly MobStateSystem _mobState = default!;
    [Dependency] private readonly EntityLookupSystem _lookup = default!;
    [Dependency] private readonly SharedInteractionSystem _interaction = default!;
    [Dependency] private readonly InventorySystem _inventory = default!;
    [Dependency] private readonly PerfumeSystem _perfume = default!;
    [Dependency] private readonly StinkyTraitSystem _stinky = default!;
    [Dependency] private readonly IGameTiming _timing = default!;

    private float _elapsed;

    public override void Initialize()
    {
        SubscribeLocalEvent<HygieneComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<HygieneComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<HygieneComponent, MeleeAttackEvent>(OnMeleeAttack);
        SubscribeLocalEvent<GunComponent, GunShotEvent>(OnGunShot);
        SubscribeLocalEvent<ShowerSprayEvent>(OnShowerSpray);
    }

    private void OnMapInit(Entity<HygieneComponent> ent, ref MapInitEvent args)
        => ApplyStage(ent, Stage(ent.Comp.Dirt));

    public override void Update(float frameTime)
    {
        _elapsed += frameTime;
        if (_elapsed < 2)
            return;

        var hours = _elapsed / 3600;
        _elapsed = 0;
        var query = EntityQueryEnumerator<HygieneComponent>();
        while (query.MoveNext(out var uid, out var hygiene))
        {
            if (HasComp<CryoSleepComponent>(uid) || !_mobState.IsAlive(uid))
            {
                ApplyStage((uid, hygiene), Stage(hygiene.Dirt));
                continue;
            }

            var moving = TryComp<InputMoverComponent>(uid, out var mover) && mover.Sprinting &&
                         mover.HasDirectionalMovement && TryComp<PhysicsComponent>(uid, out var physics) &&
                         physics.LinearVelocity.LengthSquared() > 1;
            var active = moving || hygiene.ActiveUntil > _timing.CurTime;
            ChangeDirt((uid, hygiene), hours * (active ? 17 : 5));

            if (hygiene.NextOdorIncident is not { } next || next > _timing.CurTime)
                continue;

            hygiene.NextOdorIncident = _timing.CurTime + TimeSpan.FromSeconds(_stinky.RollIncidentDelay());
            if (!HasComp<StinkyTraitComponent>(uid) && !IsOdorMasked(uid))
                _stinky.AnnounceStench(uid);
        }
    }

    private void OnMeleeAttack(Entity<HygieneComponent> ent, ref MeleeAttackEvent args)
        => ent.Comp.ActiveUntil = _timing.CurTime + TimeSpan.FromSeconds(10);

    private void OnGunShot(Entity<GunComponent> ent, ref GunShotEvent args)
    {
        if (TryComp<HygieneComponent>(args.User, out var hygiene))
            hygiene.ActiveUntil = _timing.CurTime + TimeSpan.FromSeconds(10);
    }

    public void OnPuddleSlip(EntityUid slipped, Solution liquid)
    {
        if (!TryComp<HygieneComponent>(slipped, out var hygiene) ||
            hygiene.NextPuddlePenalty > _timing.CurTime ||
            liquid.Volume <= 0 || liquid.GetTotalPrototypeQuantity("Water") == liquid.Volume)
            return;

        hygiene.NextPuddlePenalty = _timing.CurTime + TimeSpan.FromMinutes(2);
        ChangeDirt((slipped, hygiene), 20);
    }

    private void OnShowerSpray(ref ShowerSprayEvent args)
    {
        if (args.Reagent != "Water" || args.Amount <= 0)
            return;

        foreach (var (uid, _) in _lookup.GetEntitiesInRange<HygieneComponent>(args.Coordinates, 0.49f))
        {
            if (_interaction.InRangeUnobstructed(args.Source, uid, range: args.Range))
                Wash(uid, 26);
        }
    }

    public bool CanWash(EntityUid uid)
    {
        return TryComp<HygieneComponent>(uid, out var hygiene) && hygiene.Dirt > 0 &&
               (!_inventory.TryGetSlotEntity(uid, "outerClothing", out var suit) ||
                !HasComp<PressureProtectionComponent>(suit) ||
                !_inventory.TryGetSlotEntity(uid, "head", out var helmet) ||
                !HasComp<PressureProtectionComponent>(helmet));
    }

    public void Wash(EntityUid uid, float amount)
    {
        if (CanWash(uid) && TryComp<HygieneComponent>(uid, out var hygiene))
            ChangeDirt((uid, hygiene), -amount);
    }

    public bool CanWashAtSink(EntityUid uid)
        => CanWash(uid) && Comp<HygieneComponent>(uid).Dirt > 33;

    public void WashAtSink(EntityUid uid)
    {
        if (CanWashAtSink(uid))
            Wash(uid, Math.Min(10, Comp<HygieneComponent>(uid).Dirt - 33));
    }

    private void ChangeDirt(Entity<HygieneComponent> ent, float amount)
    {
        ent.Comp.Dirt = Math.Clamp(ent.Comp.Dirt + amount, 0, 100);
        ApplyStage(ent, Stage(ent.Comp.Dirt));
    }

    private void ApplyStage(Entity<HygieneComponent> ent, int stage)
    {
        if (ent.Comp.AppliedStage == stage)
            return;

        ent.Comp.AppliedStage = stage;
        _alerts.ShowAlert(ent, StageAlerts[stage]);
        ent.Comp.NextOdorIncident = stage == FilthyStage
            ? _timing.CurTime + TimeSpan.FromSeconds(_stinky.RollIncidentDelay())
            : null;
    }

    private static int Stage(float dirt) => dirt >= 100 ? FilthyStage : dirt >= 66 ? 2 : dirt >= 33 ? 1 : 0;

    private bool IsOdorMasked(EntityUid uid)
        => _stinky.HasAirFreshener(uid) || _perfume.TryGetFragrance(uid, uid, out _);

    private void OnExamined(Entity<HygieneComponent> ent, ref ExaminedEvent args)
    {
        if (!args.IsInDetailsRange || args.Examiner == ent.Owner || ent.Comp.Dirt < 66)
            return;

        if (Stage(ent.Comp.Dirt) == FilthyStage && !HasComp<StinkyTraitComponent>(ent) && !IsOdorMasked(ent))
            _stinky.DescribeStench(ent, args);
        else
            args.PushText(Loc.GetString("hygiene-examine-dirty"));
    }
}

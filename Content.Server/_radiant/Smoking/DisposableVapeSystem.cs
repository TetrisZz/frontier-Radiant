using Content.Server.Body.Systems;
using Content.Shared.Body.Components;
using Content.Shared.Chemistry.Components;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Inventory;
using Content.Shared.Mobs.Systems;
using Content.Shared.Nutrition;
using Content.Shared.Popups;
using Content.Server.Atmos.EntitySystems;
using Content.Shared.Atmos;
using Content.Shared.ActionBlocker;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared._radiant.Smoking;

namespace Content.Server._radiant.Smoking;

[RegisterComponent]
public sealed partial class DisposableVapeComponent : Component
{
    [DataField] public int PuffsRemaining = 30;
    [DataField] public float PuffInterval = 5;
    [DataField] public float Cooldown;
    [DataField] public float VaporMoles = 10f / 300f;
    [DataField] public FixedPoint2 NicotinePerPuff = FixedPoint2.New(.2);
    [DataField] public string Flavor = "disposable-vape-flavor-berry";
}

/// <summary>Sealed, finite vape. Manual puffs from the mask slot or when used on oneself.</summary>
public sealed partial class DisposableVapeSystem : EntitySystem
{
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private ActionBlockerSystem _blocker = default!;
    [Dependency] private AtmosphereSystem _atmos = default!;
    [Dependency] private InventorySystem _inventory = default!;
    [Dependency] private BloodstreamSystem _blood = default!;
    [Dependency] private MobStateSystem _mobs = default!;
    [Dependency] private SharedPopupSystem _popup = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<DisposableVapeComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<DisposableVapeComponent, AfterInteractEvent>(OnSelfUse);
        SubscribeLocalEvent<HandsComponent, UseWornVapeEvent>(OnWornUse);
    }

    private void OnWornUse(Entity<HandsComponent> ent, ref UseWornVapeEvent args)
    {
        if (args.Handled || _hands.TryGetActiveItem(ent.Owner, out _)
            || !_inventory.TryGetSlotEntity(ent.Owner, "mask", out var mask)
            || !TryComp<DisposableVapeComponent>(mask, out var vape))
            return;
        args.Handled = true;
        TryPuff(mask.Value, ent.Owner, vape);
    }

    private void OnSelfUse(Entity<DisposableVapeComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target != args.User)
            return;
        args.Handled = true;
        TryPuff(ent.Owner, args.User, ent.Comp, allowHeld: true);
    }

    private void OnExamine(Entity<DisposableVapeComponent> ent, ref ExaminedEvent args)
    {
        args.PushMarkup(Loc.GetString("disposable-vape-examine",
            ("count", ent.Comp.PuffsRemaining), ("flavor", Loc.GetString(ent.Comp.Flavor))));
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<DisposableVapeComponent>();
        while (query.MoveNext(out var uid, out var vape))
        {
            if (MetaData(uid).EntityPaused)
                continue;
            vape.Cooldown = Math.Max(0, vape.Cooldown - frameTime);
        }
    }

    public bool TryPuff(EntityUid uid, EntityUid smoker, DisposableVapeComponent? vape = null, bool allowHeld = false)
    {
        if (!Resolve(uid, ref vape) || MetaData(uid).EntityPaused || !_mobs.IsAlive(smoker)
            || !_blocker.CanInteract(smoker, uid)
            || !TryComp<BloodstreamComponent>(smoker, out var blood))
            return false;
        var worn = _inventory.TryGetSlotEntity(smoker, "mask", out var mask) && mask == uid;
        var held = allowHeld && _hands.TryGetActiveItem(smoker, out var item) && item == uid;
        if (!worn && !held)
            return false;
        if (vape.PuffsRemaining <= 0)
        {
            _popup.PopupEntity(Loc.GetString("disposable-vape-empty"), smoker, smoker);
            return false;
        }
        if (vape.Cooldown > 0)
        {
            _popup.PopupEntity(Loc.GetString("disposable-vape-cooldown",
                ("seconds", (int) Math.Ceiling(vape.Cooldown))), smoker, smoker);
            return false;
        }
        var attempt = new IngestionAttemptEvent(SlotFlags.MASK | SlotFlags.HEAD);
        RaiseLocalEvent(smoker, ref attempt);
        if (attempt.Cancelled)
        {
            _popup.PopupEntity(Loc.GetString("disposable-vape-mouth-blocked"), smoker, smoker);
            return false;
        }
        var dose = new Solution();
        dose.AddReagent("Nicotine", vape.NicotinePerPuff);
        if (!_blood.TryAddToChemicals((smoker, blood), dose))
            return false;
        vape.PuffsRemaining--;
        vape.Cooldown = vape.PuffInterval;
        Spawn("EffectDisposableVapePuff", Transform(smoker).Coordinates);
        // Match the base vape: one full 10u tank / reduction factor 300, as water vapor.
        if (_atmos.GetContainingMixture(smoker, true, true) is { } environment)
        {
            var vapor = new GasMixture(1) { Temperature = environment.Temperature };
            vapor.SetMoles(Gas.WaterVapor, vape.VaporMoles);
            _atmos.Merge(environment, vapor);
        }
        if (vape.PuffsRemaining == 0)
            _popup.PopupEntity(Loc.GetString("disposable-vape-empty"), smoker, smoker);
        else
            _popup.PopupEntity(Loc.GetString("disposable-vape-puff", ("flavor", Loc.GetString(vape.Flavor))), smoker, smoker);
        return true;
    }
}

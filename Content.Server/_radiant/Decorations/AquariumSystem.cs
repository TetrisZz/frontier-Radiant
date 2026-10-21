using System.Linq;
using Content.Shared._radiant.Decorations;
using Content.Shared._radiant.Fishing.Components;
using Content.Shared._radiant.Fishing.Systems;
using Content.Server.Body.Components;
using Content.Server.Body.Systems;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Examine;
using Content.Shared.FixedPoint;
using Content.Shared.Interaction;
using Content.Shared.Mobs;
using Content.Shared.Mobs.Systems;
using Content.Shared.Popups;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Decorations;

public sealed class AquariumSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly DamageableSystem _damage = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly FishRespirationSystem _fishRespiration = default!;
    [Dependency] private readonly RespiratorSystem _respirators = default!;
    private TimeSpan _nextUpdate;

    public override void Initialize()
    {
        UpdatesBefore.Add(typeof(RespiratorSystem));
        SubscribeLocalEvent<AquariumComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<AquariumComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<AquariumComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<AquariumComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<AquariumComponent, SolutionContainerChangedEvent>(OnSolutionChanged);
        SubscribeLocalEvent<AquariumComponent, ExaminedEvent>(OnExamined);
        SubscribeLocalEvent<AquariumFeedComponent, AfterInteractEvent>(OnFeed);
        SubscribeLocalEvent<AquariumFeedComponent, ExaminedEvent>(OnFeedExamined);
        SubscribeLocalEvent<FishComponent, MapInitEvent>(OnFishInit);
        SubscribeLocalEvent<FishComponent, MobStateChangedEvent>(OnFishStateChanged);
    }

    public bool HasWater(EntityUid uid) =>
        _solutions.TryGetSolution(uid, "tank", out _, out var solution) && solution.GetTotalPrototypeQuantity("Water") >= 50;

    public void UpdateContents(EntityUid uid)
    {
        var hasWater = HasWater(uid);
        var fish = Array.Empty<string>();
        var alive = Array.Empty<bool>();
        var decorations = Array.Empty<string>();
        var colors = Array.Empty<Color>();
        var juveniles = Array.Empty<bool>();
        if (_containers.TryGetContainer(uid, "storagebase", out var container))
        {
            var occupants = container.ContainedEntities.Where(HasComp<FishComponent>).Take(4).ToArray();
            foreach (var occupant in occupants)
                SetWaterState((occupant, Comp<FishComponent>(occupant)), hasWater);
            fish = occupants.Select(e => Comp<FishComponent>(e).AquariumState).ToArray();
            alive = occupants.Select(e => !_mobs.IsDead(e)).ToArray();
            colors = occupants.Select(e => TryComp<FishBreedingComponent>(e, out var genes) ? genes.Tint : Color.White).ToArray();
            juveniles = occupants.Select(e => TryComp<FishBreedingComponent>(e, out var genes) && genes.Juvenile).ToArray();
            decorations = container.ContainedEntities.Where(HasComp<AquariumDecorationComponent>).Select(e => Comp<AquariumDecorationComponent>(e).State).Distinct().ToArray();
        }
        // Keep dynamically typed appearance data in a registered network payload.
        _appearance.SetData(uid, AquariumVisuals.Fish, new AquariumFishVisualState(fish, alive, colors, juveniles));
        _appearance.SetData(uid, AquariumVisuals.Decorations, decorations);
        _appearance.SetData(uid, AquariumVisuals.Water, hasWater);
    }

    private void OnInit(Entity<AquariumComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.NextFeeding = _timing.CurTime + TimeSpan.FromMinutes(1);
        UpdateContents(ent);
    }

    private void OnInserted(Entity<AquariumComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == "storagebase" && HasWater(ent))
            RecoverFish(args.Entity);
        UpdateContents(ent);
    }

    private void OnRemoved(Entity<AquariumComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == "storagebase" && TryComp<FishComponent>(args.Entity, out var fish))
            SetWaterState((args.Entity, fish), false);
        UpdateContents(ent);
    }

    private void OnSolutionChanged(Entity<AquariumComponent> ent, ref SolutionContainerChangedEvent args)
    {
        if (args.SolutionId == "tank")
        {
            UpdateContents(ent);
            if (HasWater(ent) && _containers.TryGetContainer(ent, "storagebase", out var container))
            {
                foreach (var fish in container.ContainedEntities)
                    RecoverFish(fish);
            }
        }
    }

    private void OnFishInit(Entity<FishComponent> ent, ref MapInitEvent args)
    {
        ent.Comp.AirGraceEnds = _timing.CurTime + ent.Comp.AirGracePeriod;
        Dirty(ent);
        UpdateRespiration(ent);
    }

    private void SetWaterState(Entity<FishComponent> ent, bool inWater)
    {
        if (ent.Comp.InAquariumWater == inWater)
            return;
        if (ent.Comp.InAquariumWater)
            ent.Comp.AirGraceEnds = _timing.CurTime + ent.Comp.AirGracePeriod;
        ent.Comp.InAquariumWater = inWater;
        Dirty(ent);
        UpdateRespiration(ent);
    }

    public void UpdateRespiration(Entity<FishComponent> ent)
    {
        if (!_fishRespiration.CanBreathe(ent.Comp) || _mobs.IsDead(ent) ||
            !TryComp<RespiratorComponent>(ent, out var respirator))
            return;

        // Oxygen comes from the water (or remaining transport reserve), not the surrounding air.
        _respirators.UpdateSaturation(ent, respirator.MaxSaturation - respirator.Saturation, respirator);
    }

    private void RecoverFish(EntityUid fish)
    {
        if (!HasComp<FishComponent>(fish) || _mobs.IsDead(fish) || !TryComp<DamageableComponent>(fish, out var damage) ||
            !damage.Damage.DamageDict.TryGetValue("Asphyxiation", out var amount) || amount <= 0)
            return;

        var recovery = new DamageSpecifier();
        recovery.DamageDict["Asphyxiation"] = -amount;
        _damage.TryChangeDamage(fish, recovery, ignoreResistances: true, interruptsDoAfters: false);
    }

    private void OnFishStateChanged(Entity<FishComponent> ent, ref MobStateChangedEvent args)
    {
        if (_containers.TryGetContainingContainer(ent.Owner, out var container) && HasComp<AquariumComponent>(container.Owner))
            UpdateContents(container.Owner);
    }

    private void OnInsertAttempt(Entity<AquariumComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID == "storagebase" && HasComp<FishComponent>(args.EntityUid) &&
            args.Container.ContainedEntities.Count(HasComp<FishComponent>) >= ent.Comp.Capacity)
            args.Cancel();
    }

    public override void Update(float frameTime)
    {
        if (_timing.CurTime < _nextUpdate)
            return;
        _nextUpdate = _timing.CurTime + TimeSpan.FromSeconds(1);
        var fishQuery = EntityQueryEnumerator<FishComponent, RespiratorComponent>();
        while (fishQuery.MoveNext(out var fishUid, out var fish, out _))
            UpdateRespiration((fishUid, fish));
        var query = EntityQueryEnumerator<AquariumComponent>();
        while (query.MoveNext(out var uid, out var aquarium))
        {
            _appearance.SetData(uid, AquariumVisuals.Water, HasWater(uid));
            if (_timing.CurTime < aquarium.NextFeeding)
                continue;
            aquarium.NextFeeding = _timing.CurTime + TimeSpan.FromMinutes(1);
            if (!HasWater(uid) || !_containers.TryGetContainer(uid, "storagebase", out var container))
                continue;
            foreach (var fish in container.ContainedEntities)
            {
                if (!HasComp<FishComponent>(fish) || _mobs.IsDead(fish))
                    continue;
                var fed = aquarium.Food > 0;
                if (fed)
                    aquarium.Food--;
                var damage = new DamageSpecifier();
                damage.DamageDict[fed ? "Asphyxiation" : "Poison"] = fed ? -1 : FixedPoint2.New(0.2f);
                if (fed)
                    damage.DamageDict["Poison"] = FixedPoint2.New(-0.2f);
                _damage.TryChangeDamage(fish, damage, ignoreResistances: true);
            }
        }
    }

    private void OnFeed(Entity<AquariumFeedComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !TryComp<AquariumComponent>(target, out var aquarium))
            return;
        args.Handled = true;
        if (ent.Comp.Uses <= 0 || aquarium.Food >= aquarium.MaxFood)
        {
            _popup.PopupEntity(Loc.GetString(ent.Comp.Uses <= 0 ? "aquarium-feed-empty" : "aquarium-feed-full"), target, args.User);
            return;
        }
        aquarium.Food = Math.Min(aquarium.MaxFood, aquarium.Food + 10);
        ent.Comp.Uses--;
        Dirty(ent);
        _popup.PopupEntity(Loc.GetString("aquarium-fed"), target, args.User);
    }

    private void OnFeedExamined(Entity<AquariumFeedComponent> ent, ref ExaminedEvent args) =>
        args.PushText(Loc.GetString("aquarium-feed-uses", ("uses", ent.Comp.Uses)));

    private void OnExamined(Entity<AquariumComponent> ent, ref ExaminedEvent args)
    {
        args.PushText(Loc.GetString(HasWater(ent) ? "aquarium-water-ready" : "aquarium-needs-water"));
        args.PushText(Loc.GetString("aquarium-food", ("food", ent.Comp.Food)));
    }
}

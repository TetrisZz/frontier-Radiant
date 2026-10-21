using System.Linq;
using Content.Server.PowerCell;
using Content.Shared._radiant.Decorations;
using Content.Shared._radiant.Fishing.Components;
using Content.Shared.Chemistry.EntitySystems;
using Content.Shared.Damage;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Interaction;
using Content.Shared.Mobs.Systems;
using Content.Shared.PowerCell;
using Content.Shared.PowerCell.Components;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;
using Robust.Shared.Timing;

namespace Content.Server._radiant.Fishing;

public sealed class FishScannerSystem : EntitySystem
{
    [Dependency] private readonly PowerCellSystem _cells = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedHandsSystem _hands = default!;
    [Dependency] private readonly SharedSolutionContainerSystem _solutions = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly MobStateSystem _mobs = default!;
    [Dependency] private readonly IGameTiming _timing = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<FishScannerComponent, AfterInteractEvent>(OnScan);
        SubscribeLocalEvent<FishScannerComponent, BeforeActivatableUIOpenEvent>(OnOpen);
        SubscribeLocalEvent<FishScannerComponent, PowerCellChangedEvent>((Entity<FishScannerComponent> ent, ref PowerCellChangedEvent _) => UpdateState(ent));
    }

    private void OnOpen(Entity<FishScannerComponent> ent, ref BeforeActivatableUIOpenEvent args) => UpdateState(ent);

    private void OnScan(Entity<FishScannerComponent> ent, ref AfterInteractEvent args)
    {
        if (args.Handled || !args.CanReach || args.Target is not { } target || !_hands.IsHolding(args.User, ent) ||
            (!HasComp<FishComponent>(target) && !HasComp<AquariumComponent>(target)))
            return;
        args.Handled = true;
        if (_timing.CurTime < ent.Comp.ReadyAt || !_cells.TryUseActivatableCharge(ent, user: args.User))
            return;
        ent.Comp.ReadyAt = _timing.CurTime + ent.Comp.ScanCooldown;
        var fish = new List<FishScanEntry>();
        var water = 0f;
        var food = 0;
        var capacity = 0;
        var aquarium = TryComp<AquariumComponent>(target, out var tank);
        if (aquarium)
        {
            food = tank!.Food;
            capacity = tank.Capacity;
            if (_solutions.TryGetSolution(target, "tank", out _, out var solution))
                water = (float) solution.GetTotalPrototypeQuantity("Water");
            if (_containers.TryGetContainer(target, "storagebase", out var contents))
                fish.AddRange(contents.ContainedEntities.Where(HasComp<FishComponent>).Select(ScanFish));
        }
        else
            fish.Add(ScanFish(target));
        ent.Comp.LastScan = new FishScannerUiState(Name(target), aquarium, water, food, capacity, fish, Charge(ent));
        UpdateState(ent);
        _ui.TryOpenUi(ent.Owner, FishScannerUiKey.Key, args.User);
        _audio.PlayPvs(new SoundPathSpecifier("/Audio/Items/Medical/healthscanner.ogg"), ent);
    }

    private FishScanEntry ScanFish(EntityUid target)
    {
        var fish = Comp<FishComponent>(target);
        var breedable = TryComp<FishBreedingComponent>(target, out var genes);
        var health = _mobs.IsDead(target) ? "fish-scanner-dead" :
            _mobs.IsCritical(target) ? "fish-scanner-critical" : "fish-scanner-alive";
        return new FishScanEntry(Name(target), !breedable ? "fish-scanner-not-breedable" :
            genes!.Female ? "fish-scanner-female" : "fish-scanner-male", genes?.Juvenile ?? false,
            breedable, fish.InAquariumWater, health,
            TryComp<DamageableComponent>(target, out var damage) ? (float) damage.TotalDamage : 0,
            genes?.Juvenile == true ? (int) Math.Clamp(100 * genes.GrowthSeconds / FishBreedingRules.GrowthDuration, 0, 100) : 100,
            genes?.Tint ?? Color.White);
    }

    private int Charge(EntityUid scanner)
        => _cells.TryGetBatteryFromSlot(scanner, out var battery) && battery.MaxCharge > 0
            ? (int) Math.Clamp(100 * battery.CurrentCharge / battery.MaxCharge, 0, 100) : 0;

    private void UpdateState(Entity<FishScannerComponent> ent)
    {
        var scan = ent.Comp.LastScan;
        _ui.SetUiState(ent.Owner, FishScannerUiKey.Key, new FishScannerUiState(scan?.Target ?? "",
            scan?.Aquarium ?? false, scan?.Water ?? 0, scan?.Food ?? 0, scan?.Capacity ?? 0,
            scan?.Fish ?? [], Charge(ent)));
    }
}

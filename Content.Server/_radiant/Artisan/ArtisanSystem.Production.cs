using System.Linq;
using Content.Shared._radiant.Artisan;
using Content.Shared._radiant.Paint;
using Content.Shared.Chemistry.Components;
using Content.Shared.FixedPoint;
using Content.Shared.Stacks;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.Artisan;

public sealed partial class ArtisanSystem
{
    private EntityUid[] Ingredients(EntityUid station, ArtisanRecipePrototype recipe) => Inputs(station)
        .Where(item => recipe.Ingredients.Any(id => MetaData(item).EntityPrototype?.ID == id.Id))
        .Take(recipe.IngredientCount).ToArray();

    private string? Blocked(Entity<ArtisanStationComponent> ent, ArtisanRecipePrototype recipe, EntityUid? piece = null)
    {
        if (!Ready(ent)) return ent.Comp.NeedsPower && !_power.IsPowered(ent) ? "artisan-no-power" : "artisan-unanchored";
        var stage = piece is { } uid && TryComp<ArtisanWorkpieceComponent>(uid, out var work) ? work.Stage : 0;
        if (stage < 0 || stage >= recipe.Steps.Count || recipe.Steps[stage].Station != ent.Comp.Kind) return "artisan-wrong-station";
        var step = recipe.Steps[stage];
        if (step.MinTemperature > ent.Comp.Temperature) return "artisan-warming";
        if (step.NeedsHeat && (piece == null || Comp<ArtisanWorkpieceComponent>(piece.Value).Heat <= 0)) return "artisan-needs-reheat";
        if (step.Water > 0 && (!TryTank(ent, out _, out var water) || water.GetTotalPrototypeQuantity("Water") < step.Water)) return "artisan-needs-water";
        if (stage == 0)
        {
            if (recipe.Stack != null && Inputs(ent).Where(item => TryComp<StackComponent>(item, out var stack) && stack.StackTypeId == recipe.Stack)
                    .Sum(item => _stacks.GetCount(item)) < recipe.Amount) return "artisan-needs-material";
            if (recipe.Ingredients.Count > 0 && Ingredients(ent, recipe).Length < recipe.IngredientCount) return "artisan-needs-ingredients";
        }
        if (recipe.Drink != null && (!TryTank(ent, out _, out var tank) || tank.AvailableVolume + step.Water < recipe.DrinkAmount)) return "artisan-tank-full";
        return null;
    }

    private bool Start(Entity<ArtisanStationComponent> ent, EntityUid user, ArtisanRecipePrototype recipe, EntityUid? item)
    {
        if (ent.Comp.Busy) return Reject(ent, user, "artisan-busy");
        if (Blocked(ent, recipe, item) is { } reason) return Reject(ent, user, reason);
        var stage = item == null ? 0 : Comp<ArtisanWorkpieceComponent>(item.Value).Stage;
        if (!HeldTool(user, recipe.Steps[stage].Tool))
            return Reject(ent, user, "artisan-needed-tool", ("tool", Loc.GetString("artisan-tool-" + recipe.Steps[stage].Tool.ToString().ToLowerInvariant())));
        ent.Comp.Recipe = recipe.ID;
        ent.Comp.Workpiece = item;
        ent.Comp.Operator = user;
        ent.Comp.Maker = Name(user);
        ent.Comp.Reheating = false;
        ent.Comp.Remaining = ent.Comp.Duration = Math.Max(1, recipe.Steps[stage].Duration);
        UpdateOperation(ent);
        _audio.PlayPvs(recipe.Steps[stage].StartSound, ent);
        return true;
    }

    private bool KeepWorking(Entity<ArtisanStationComponent> ent)
    {
        if (ent.Comp.Bottling) return true;
        if (ent.Comp.Workpiece is { } uid && (!Exists(uid) || !Inputs(ent).Contains(uid))) return false;
        if (ent.Comp.Reheating) return true;
        if (ent.Comp.Recipe == null || !_prototypes.TryIndex<ArtisanRecipePrototype>(ent.Comp.Recipe, out var recipe)) return false;
        var stage = ent.Comp.Workpiece is { } piece ? Comp<ArtisanWorkpieceComponent>(piece).Stage : 0;
        if (stage >= recipe.Steps.Count) return false;
        var step = recipe.Steps[stage];
        var manual = step.Station is ArtisanStationKind.Pottery or ArtisanStationKind.Anvil or ArtisanStationKind.GlassBench or ArtisanStationKind.Quench or ArtisanStationKind.Sculpting;
        return (!manual || ent.Comp.Operator is { } user && Exists(user) &&
            _interaction.InRangeUnobstructed(user, ent.Owner) && HeldTool(user, step.Tool)) &&
            (!step.NeedsHeat || ent.Comp.Workpiece is { } hot && Comp<ArtisanWorkpieceComponent>(hot).Heat > 0);
    }

    private void Complete(Entity<ArtisanStationComponent> ent)
    {
        if (ent.Comp.Bottling)
        { Bottle(ent); return; }
        if (ent.Comp.Reheating)
        {
            if (ent.Comp.Workpiece is { } item && TryComp<ArtisanWorkpieceComponent>(item, out var hot))
            { hot.Heat = 120; Dirty(item, hot); }
            Cancel(ent);
            return;
        }
        if (ent.Comp.Recipe == null || !_prototypes.TryIndex<ArtisanRecipePrototype>(ent.Comp.Recipe, out var recipe) || Blocked(ent, recipe, ent.Comp.Workpiece) != null)
        { Cancel(ent); return; }
        var pieceUid = ent.Comp.Workpiece;
        var stage = pieceUid is { } existing ? Comp<ArtisanWorkpieceComponent>(existing).Stage : 0;
        var step = recipe.Steps[stage];
        if (step.Water > 0 && TryTank(ent, out var waterEntity, out _)) _solutions.RemoveReagent(waterEntity, "Water", step.Water);
        if (stage == 0 && !ConsumeMaterials(ent, recipe)) { Cancel(ent); return; }
        if (recipe.Drink is { } drink && TryTank(ent, out var tank, out _))
        {
            _solutions.TryAddReagent(tank, drink, recipe.DrinkAmount);
            if (ent.Comp.BatchName != "") _labels.Label(ent, ent.Comp.BatchName);
            SetTapMode(ent, true);
            Cancel(ent);
            return;
        }
        _audio.PlayPvs(step.FinishSound, ent);
        if (stage + 1 >= recipe.Steps.Count)
        {
            var result = Spawn(recipe.Result, Transform(ent).Coordinates);
            var product = EnsureComp<ArtisanProductComponent>(result);
            product.Maker = pieceUid is { } original ? Comp<ArtisanWorkpieceComponent>(original).Maker : ent.Comp.Maker;
            if (pieceUid is { } painted && TryComp<WashablePaintComponent>(painted, out var glaze)) product.Glaze = glaze.Color;
            Dirty(result, product);
            if (pieceUid is { } finished) Del(finished);
            _containers.Insert(result, _containers.GetContainer(ent, ArtisanStationComponent.Container));
        }
        else
        {
            if (pieceUid == null)
            {
                if (recipe.Blank is not { } blank) { Cancel(ent); return; }
                var created = EntityManager.CreateEntityUninitialized(blank, Transform(ent).Coordinates);
                var work = Comp<ArtisanWorkpieceComponent>(created);
                work.Recipe = recipe.ID; work.Stage = 1; work.Maker = ent.Comp.Maker; work.Heat = step.GivesHeat ? 120 : 0;
                EntityManager.InitializeAndStartEntity(created);
                _metadata.SetEntityName(created, Loc.GetString("artisan-workpiece-name", ("product", Loc.GetString(recipe.Name).Split(':')[0])));
                _containers.Insert(created, _containers.GetContainer(ent, ArtisanStationComponent.Container));
            }
            else
            {
                var work = Comp<ArtisanWorkpieceComponent>(pieceUid.Value);
                work.Stage++;
                if (step.GivesHeat) work.Heat = 120;
                Dirty(pieceUid.Value, work);
            }
        }
        Cancel(ent);
    }

    private bool ConsumeMaterials(EntityUid uid, ArtisanRecipePrototype recipe)
    {
        // Validation precedes all consumption, and the input container remains locked throughout the job.
        if (recipe.Stack != null)
        {
            var remaining = recipe.Amount;
            foreach (var item in Inputs(uid).ToArray())
            {
                if (!TryComp<StackComponent>(item, out var stack) || stack.StackTypeId != recipe.Stack) continue;
                var used = Math.Min(remaining, _stacks.GetCount(item));
                if (!_stacks.Use(item, used)) return false;
                remaining -= used;
                if (remaining == 0) break;
            }
            if (remaining != 0) return false;
        }
        foreach (var ingredient in Ingredients(uid, recipe)) Del(ingredient);
        return true;
    }

    private string? StartBottling(Entity<ArtisanStationComponent> ent, bool chooseSource = true)
    {
        if (ent.Comp.Kind != ArtisanStationKind.Bottler) return "artisan-wrong-station";
        if (!Ready(ent)) return Transform(ent).Anchored ? "artisan-no-power" : "artisan-unanchored";
        if (chooseSource && (ent.Comp.LiquidSource == null || !Exists(ent.Comp.LiquidSource.Value) || !Inputs(ent).Contains(ent.Comp.LiquidSource.Value)))
            ent.Comp.LiquidSource = Inputs(ent).Where(item => _solutions.TryGetFitsInDispenser(item, out _, out var contents) && contents.Volume > 0).Cast<EntityUid?>().FirstOrDefault();
        if (ent.Comp.LiquidSource is not { } source || !Inputs(ent).Contains(source) ||
            !_solutions.TryGetFitsInDispenser(source, out _, out var solution) || solution.Volume == 0) return "artisan-no-source";
        if (solution.Volume < ent.Comp.BottleAmount) return "artisan-not-enough-drink";
        if (EmptyBottle(ent) == null) return "artisan-no-empty-bottle";
        ent.Comp.Bottling = true;
        ent.Comp.Remaining = ent.Comp.Duration = 3;
        UpdateOperation(ent);
        return null;
    }

    private EntityUid? EmptyBottle(Entity<ArtisanStationComponent> ent) => Inputs(ent).Where(item => item != ent.Comp.LiquidSource)
        .Cast<EntityUid?>().FirstOrDefault(item => item != null && _solutions.TryGetFitsInDispenser(item.Value, out _, out var solution) &&
            solution.Volume == 0 && solution.AvailableVolume >= ent.Comp.BottleAmount);

    private void Bottle(Entity<ArtisanStationComponent> ent)
    {
        if (ent.Comp.LiquidSource is not { } source || !Inputs(ent).Contains(source) ||
            !_solutions.TryGetFitsInDispenser(source, out var sourceEntity, out var liquid) ||
            liquid.Volume < ent.Comp.BottleAmount || EmptyBottle(ent) is not { } bottle ||
            !_solutions.TryGetFitsInDispenser(bottle, out var target, out _))
        { Cancel(ent); return; }
        var split = _solutions.SplitSolution(sourceEntity.Value, FixedPoint2.New(ent.Comp.BottleAmount));
        if (!_solutions.TryAddSolution(target.Value, split))
        {
            _solutions.TryAddSolution(sourceEntity.Value, split);
            Cancel(ent); return;
        }
        if (ent.Comp.BatchName != "") _labels.Label(bottle, ent.Comp.BatchName);
        ent.Comp.Bottling = false;
        StartBottling(ent, chooseSource: false);
    }
}

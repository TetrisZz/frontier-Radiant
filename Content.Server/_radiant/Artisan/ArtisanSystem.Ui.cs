using System.Linq;
using Content.Shared._radiant.Artisan;
using Content.Shared.Chemistry.Reagent;
using Content.Shared.Stacks;

namespace Content.Server._radiant.Artisan;

public sealed partial class ArtisanSystem
{
    private void UpdateUi(Entity<ArtisanStationComponent> ent)
    {
        if (!_ui.IsUiOpen(ent.Owner, ArtisanUiKey.Key)) return;
        var recipes = new List<ArtisanRecipeEntry>();
        foreach (var recipe in _prototypes.EnumeratePrototypes<ArtisanRecipePrototype>().Where(recipe => recipe.Steps.Count > 0 && recipe.Steps[0].Station == ent.Comp.Kind))
        {
            var reason = ent.Comp.Busy ? "artisan-busy" : Blocked(ent, recipe);
            recipes.Add(new(recipe.ID, Loc.GetString(recipe.Name), reason == null, reason == null ? "" : Loc.GetString(reason),
                recipe.Result.Id, Requirements(ent, recipe, 0)));
        }
        var items = new List<ArtisanItemEntry>();
        foreach (var item in Inputs(ent))
        {
            var next = "";
            var process = false;
            var hot = false;
            var result = "";
            var requirements = new List<ArtisanRequirementEntry>();
            if (TryComp<ArtisanWorkpieceComponent>(item, out var piece) && _prototypes.TryIndex(piece.Recipe, out var recipe))
            {
                hot = piece.Heat > 0;
                result = recipe.Result.Id;
                if (piece.Stage < recipe.Steps.Count)
                {
                    var step = recipe.Steps[piece.Stage];
                    requirements = Requirements(ent, recipe, piece.Stage);
                    next = Loc.GetString("artisan-next-step", ("step", Loc.GetString(step.Name)));
                    var reason = Blocked(ent, recipe, item);
                    process = !ent.Comp.Busy && reason == null;
                    if (reason != null) next += "\n" + Loc.GetString(reason);
                    if (step.Tool != ArtisanToolKind.None)
                        next += "\n" + Loc.GetString("artisan-needed-tool", ("tool", Loc.GetString("artisan-tool-" + step.Tool.ToString().ToLowerInvariant())));
                }
                if (recipe.Steps[0].Station != ArtisanStationKind.Sculpting)
                    next += "\n" + Loc.GetString(recipe.Steps[0].Station == ArtisanStationKind.Furnace ? hot ? "artisan-hot" : "artisan-cold" : "artisan-unfired",
                        ("seconds", (int)Math.Ceiling(piece.Heat)));
            }
            if (ent.Comp.Kind == ArtisanStationKind.Bottler && _solutions.TryGetFitsInDispenser(item, out _, out var contents))
                next = Loc.GetString("artisan-volume", ("amount", contents.Volume)) + (ent.Comp.LiquidSource == item ? " · " + Loc.GetString("artisan-source") : "");
            items.Add(new(GetNetEntity(item), Name(item), next, process,
                !ent.Comp.Busy && HasComp<ArtisanWorkpieceComponent>(item) && ent.Comp.Kind == ArtisanStationKind.Furnace && Ready(ent) && ent.Comp.Temperature >= 700, hot,
                MetaData(item).EntityPrototype?.ID ?? "", result, requirements));
        }
        var liquids = "";
        if (TryTank(ent, out _, out var tank))
            liquids = Loc.GetString("artisan-volume", ("amount", tank.Volume)) + "\n" + string.Join(", ", tank.Contents.Select(reagent =>
                _prototypes.TryIndex<ReagentPrototype>(reagent.Reagent.Prototype, out var prototype) ? prototype.LocalizedName + ": " + reagent.Quantity : reagent.Reagent.Prototype));
        var status = ent.Comp.Busy ? !Ready(ent) ? "artisan-paused" : "artisan-busy" : !Ready(ent) ?
            ent.Comp.NeedsPower && !_power.IsPowered(ent) ? "artisan-no-power" : "artisan-unanchored" : "artisan-ready";
        _ui.SetUiState(ent.Owner, ArtisanUiKey.Key, new ArtisanUiState(Name(ent), ent.Comp.Kind, Loc.GetString(status),
            ent.Comp.Busy && ent.Comp.Duration > 0 ? 1 - ent.Comp.Remaining / ent.Comp.Duration : 0,
            ent.Comp.Temperature, liquids, recipes, items, ent.Comp.BatchName, ent.Comp.BottleAmount, ent.Comp.Busy, ent.Comp.DrainMode));
    }

    private List<ArtisanRequirementEntry> Requirements(Entity<ArtisanStationComponent> ent, ArtisanRecipePrototype recipe, int stage)
    {
        var result = new List<ArtisanRequirementEntry>();
        if (stage < 0 || stage >= recipe.Steps.Count) return result;
        if (stage == 0 && recipe.Stack is { } stackId && _prototypes.TryIndex<StackPrototype>(stackId, out var stack))
        {
            var have = Inputs(ent).Where(item => TryComp<StackComponent>(item, out var data) && data.StackTypeId == stackId)
                .Sum(item => _stacks.GetCount(item));
            result.Add(new(Loc.GetString("artisan-ui-requirement", ("name", Loc.GetString(stack.Name)),
                ("have", have), ("need", recipe.Amount)), stack.Spawn.Id, have >= recipe.Amount));
        }
        if (stage == 0 && recipe.Ingredients.Count > 0)
        {
            var have = Ingredients(ent, recipe).Length;
            var names = string.Join(" / ", recipe.Ingredients.Select(id => _prototypes.Index(id).Name));
            result.Add(new(Loc.GetString("artisan-ui-requirement", ("name", names), ("have", have),
                ("need", recipe.IngredientCount)), recipe.Ingredients[0].Id, have >= recipe.IngredientCount));
        }
        var step = recipe.Steps[stage];
        if (step.Water > 0)
        {
            var have = TryTank(ent, out _, out var water) ? water.GetTotalPrototypeQuantity("Water") : 0;
            result.Add(new(Loc.GetString("artisan-ui-water-requirement", ("have", have), ("need", step.Water)),
                null, have >= step.Water));
        }
        if (step.MinTemperature > 0)
            result.Add(new(Loc.GetString("artisan-ui-heat-requirement", ("have", (int)ent.Comp.Temperature),
                ("need", (int)step.MinTemperature)), null, ent.Comp.Temperature >= step.MinTemperature));
        var tool = step.Tool switch
        {
            ArtisanToolKind.Tongs => "RadiantArtisanTongs",
            ArtisanToolKind.Hammer => "RadiantArtisanHammer",
            ArtisanToolKind.Blowpipe => "RadiantArtisanBlowpipe",
            ArtisanToolKind.Paddle => "RadiantArtisanPaddle",
            ArtisanToolKind.Chisel => "RadiantSculptingChisel",
            _ => null
        };
        if (tool != null)
            result.Add(new(Loc.GetString("artisan-needed-tool", ("tool", Loc.GetString("artisan-tool-" + step.Tool.ToString().ToLowerInvariant()))), tool, null));
        return result;
    }
}

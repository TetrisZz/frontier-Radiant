using System.Linq;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Robust.Shared.Map;
using Robust.Shared.Prototypes;

namespace Content.Server._radiant.ResearchBlueprints;

public sealed partial class BlueprintResearchSystem
{
    private void OnBlueprintMapInit(Entity<BlueprintComponent> ent, ref MapInitEvent args)
        => InitializeBlueprint(ent);

    public void InitializeBlueprint(Entity<BlueprintComponent> ent)
    {
        // Saved or explicitly configured results must never be rolled again on map load.
        if (ent.Comp.Modifiers != null)
        {
            if (string.IsNullOrWhiteSpace(ent.Comp.Modifiers.InstanceId))
                SetBlueprint(ent, ent.Comp.ProvidedRecipes, ent.Comp.Modifiers);
            return;
        }
        if (ent.Comp.ProvidedRecipes.Count == 0)
            return;

        var basis = new BlueprintModifiers
        {
            RareLoc = ent.Comp.Rarity, Lathe = ent.Comp.ModifierLathe,
            AdditionalLathes = new(ent.Comp.ModifierAdditionalLathes),
        };
        if (ent.Comp.RandomizeModifiers && _proto.TryIndex(ent.Comp.ModifierPreset, out var settings))
        {
            // Keep the exact supplied recipes, even if no research technology contains them.
            var required = ent.Comp.ProvidedRecipes.Where(r => _proto.HasIndex(r))
                .SelectMany(r => _proto.Index(r).Materials.Keys).ToHashSet();
            basis = BlueprintResearchRoller.Roll(_random, settings,
                settings.MaterialTiers.Select(t => _proto.Index(t)).ToArray(), MachinesFor(basis.Lathe), required, basis);
        }
        SetBlueprint(ent, ent.Comp.ProvidedRecipes, basis);
    }

    private List<EntProtoId> MachinesFor(EntProtoId? lathe)
    {
        if (lathe is { } machine &&
            _proto.Index(machine).TryGetComponent<LatheComponent>(out var data, EntityManager.ComponentFactory) &&
            data.LatheBlueprint is { } group && _proto.TryIndex(group, out var machines))
            return machines.Lathe;
        return [];
    }

    private void OnResearchSpawnerMapInit(Entity<BlueprintResearchSpawnerComponent> ent, ref MapInitEvent args)
    {
        if (_proto.TryIndex(ent.Comp.Preset, out var settings))
            TryCreateBlueprint(Transform(ent).Coordinates, settings, out _, out _);
        QueueDel(ent);
    }

    public bool TryCreateBlueprint(EntityCoordinates coordinates, BlueprintResearchPrototype settings,
        out Entity<BlueprintComponent> blueprint, out TechnologyBlueprintsPrototype technology)
    {
        blueprint = default;
        technology = null!;
        var technologies = _proto.EnumeratePrototypes<TechnologyBlueprintsPrototype>()
            .Where(settings.Allows).OrderBy(t => t.ID).ToArray();
        if (technologies.Length == 0)
            return false;

        technology = BlueprintResearchRoller.PickWeighted(_random, technologies, t => 1 / t.Rare);
        var recipes = BlueprintResearchRoller.RollRecipes(_random, technology);
        if (recipes.Any(r => !_proto.HasIndex(r)))
            return false;

        var required = recipes.SelectMany(r => _proto.Index(r).Materials.Keys).ToHashSet();
        var uid = Spawn(settings.BlueprintFor(technology), coordinates);
        blueprint = (uid, EnsureComp<BlueprintComponent>(uid));
        var modifiers = new BlueprintModifiers
        {
            Technology = technology.ID, RareLoc = technology.RareLoc, Lathe = technology.Lathe,
            AdditionalLathes = new(technology.AdditionalLathes),
        };
        if (blueprint.Comp.RandomizeModifiers)
            modifiers = BlueprintResearchRoller.Roll(_random, settings,
                settings.MaterialTiers.Select(t => _proto.Index(t)).ToArray(), MachinesFor(technology.Lathe), required, modifiers);
        SetBlueprint(blueprint, recipes, modifiers);
        var item = string.Join(", ", recipes.OrderBy(r => r.Id).Select(recipe => _lathes.GetRecipeName(_proto.Index(recipe))));
        _metadata.SetEntityName(uid, Loc.GetString("research-blueprint-paper-name", ("item", item)));
        return true;
    }
}

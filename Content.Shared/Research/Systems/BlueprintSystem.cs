using Content.Shared.Examine;
using Content.Shared.IdentityManagement;
using Content.Shared.Interaction;
using Content.Shared.Lathe;
using Content.Shared.Popups;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.Whitelist;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using System.Linq;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared.Access.Systems;

namespace Content.Shared.Research.Systems;

public sealed class BlueprintSystem : EntitySystem
{
    [Dependency] private readonly SharedContainerSystem _container = default!;
    [Dependency] private readonly EntityWhitelistSystem _entityWhitelist = default!;
    [Dependency] private readonly SharedPopupSystem _popup = default!;
    [Dependency] private readonly IPrototypeManager _proto = default!; // Frontier
    [Dependency] private readonly SharedLatheSystem _lathe = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;

    private const int MaxExaminedRecipes = 5; // Frontier

    /// <inheritdoc/>
    public override void Initialize()
    {
        SubscribeLocalEvent<BlueprintReceiverComponent, ComponentStartup>(OnStartup);
        SubscribeLocalEvent<BlueprintReceiverComponent, AfterInteractUsingEvent>(OnAfterInteract);
        SubscribeLocalEvent<BlueprintReceiverComponent, LatheGetRecipesEvent>(OnGetRecipes);
        SubscribeLocalEvent<BlueprintComponent, ExaminedEvent>(OnBlueprintExamined); // Frontier
    }

    private void OnStartup(Entity<BlueprintReceiverComponent> ent, ref ComponentStartup args)
    {
        _container.EnsureContainer<Container>(ent, ent.Comp.ContainerId);
    }

    private void OnAfterInteract(Entity<BlueprintReceiverComponent> ent, ref AfterInteractUsingEvent args)
    {
        if (args.Handled || !args.CanReach || !TryComp<BlueprintComponent>(args.Used, out var blueprintComponent))
            return;
        args.Handled = TryInsertBlueprint(ent, (args.Used, blueprintComponent), args.User);
    }

    private void OnGetRecipes(Entity<BlueprintReceiverComponent> ent, ref LatheGetRecipesEvent args)
    {
        if (HasComp<BlueprintLibraryComponent>(ent))
            return;
        foreach (var blueprint in _container.GetContainer(ent, ent.Comp.ContainerId).ContainedEntities)
        {
            if (!TryComp<BlueprintComponent>(blueprint, out var data) ||
                data.Modifiers != null && !_lathe.MatchesBlueprintLathe(ent, args.Comp, data.Modifiers))
                continue;
            args.Recipes.UnionWith(data.ProvidedRecipes);
        }
    }

    public bool TryInsertBlueprint(Entity<BlueprintReceiverComponent> ent, Entity<BlueprintComponent> blueprint, EntityUid? user)
    {
        if (!CanInsertBlueprint(ent, blueprint, user))
            return false;

        if (!_container.Insert(blueprint.Owner, _container.GetContainer(ent, ent.Comp.ContainerId)))
            return false;

        if (user is not null)
        {
            var userId = Identity.Entity(user.Value, EntityManager);
            var bpId = Identity.Entity(blueprint, EntityManager);
            var machineId = Identity.Entity(ent, EntityManager);
            var msg = Loc.GetString("blueprint-receiver-popup-insert",
                ("user", userId),
                ("blueprint", bpId),
                ("receiver", machineId));
            _popup.PopupPredicted(msg, ent, user);
        }

        var ev = new TechnologyDatabaseModifiedEvent(blueprint.Comp.ProvidedRecipes.Select(it => it.Id).ToList());
        RaiseLocalEvent(ent, ref ev);
        return true;
    }

    public bool CanInsertBlueprint(Entity<BlueprintReceiverComponent> ent, Entity<BlueprintComponent> blueprint, EntityUid? user)
    {
        if (user is { } actor && !_access.IsAllowed(actor, ent))
        {
            _popup.PopupPredicted(Loc.GetString("comms-console-permission-denied"), ent, user);
            return false;
        }
        if (_entityWhitelist.IsWhitelistFail(ent.Comp.Whitelist, blueprint))
        {
            _popup.PopupPredicted(Loc.GetString("blueprint-receiver-popup-invalid-type"), ent, user); // Frontier
            return false;
        }

        if (blueprint.Comp.ProvidedRecipes.Count == 0)
        {
            Log.Error($"Attempted to insert blueprint {ToPrettyString(blueprint)} with no recipes.");
            _popup.PopupPredicted(Loc.GetString("blueprint-receiver-popup-no-recipes"), ent, user); // Frontier
            return false;
        }

        if (blueprint.Comp.Modifiers != null && !HasComp<BlueprintLibraryComponent>(ent) &&
            (!TryComp<LatheComponent>(ent, out var lathe) || !_lathe.MatchesBlueprintLathe(ent, lathe, blueprint.Comp.Modifiers)))
        {
            _popup.PopupPredicted(Loc.GetString("research-blueprint-wrong-lathe"), ent, user);
            return false;
        }

        // Don't add new blueprints if there are no new recipes.
        var currentRecipes = GetBlueprintRecipes(ent);
        if (!HasComp<BlueprintLibraryComponent>(ent) && blueprint.Comp.Modifiers == null &&
            currentRecipes.Count != 0 && currentRecipes.IsSupersetOf(blueprint.Comp.ProvidedRecipes))
        {
            _popup.PopupPredicted(Loc.GetString("blueprint-receiver-popup-recipe-exists"), ent, user);
            return false;
        }

        return _container.CanInsert(blueprint, _container.GetContainer(ent, ent.Comp.ContainerId));
    }

    public HashSet<ProtoId<LatheRecipePrototype>> GetBlueprintRecipes(Entity<BlueprintReceiverComponent> ent)
    {
        var contained = _container.GetContainer(ent, ent.Comp.ContainerId);

        var recipes = new HashSet<ProtoId<LatheRecipePrototype>>();
        foreach (var blueprint in contained.ContainedEntities)
        {
            if (!TryComp<BlueprintComponent>(blueprint, out var blueprintComponent))
                continue;

            foreach (var provided in blueprintComponent.ProvidedRecipes)
            {
                recipes.Add(provided);
            }
        }

        return recipes;
    }

    // Frontier
    public void OnBlueprintExamined(Entity<BlueprintComponent> ent, ref ExaminedEvent args)
    {
        using (args.PushGroup(nameof(BlueprintComponent)))
        {
            if (ent.Comp.ProvidedRecipes.Count <= 0)
            {
                args.PushMarkup(Loc.GetString("blueprint-description-none"));
                return;
            }

            args.PushMarkup(Loc.GetString("blueprint-description"));
            int count = 0;
            foreach (var recipe in ent.Comp.ProvidedRecipes)
            {
                if (!_proto.TryIndex(recipe, out var proto))
                    continue;

                string name;
                if (proto.Name != null)
                    name = Loc.GetString(proto.Name);
                else if (_proto.TryIndex(proto.Result, out var prototype))
                    name = prototype.Name;
                else
                    continue;

                args.PushMarkup(Loc.GetString("blueprint-description-item", ("name", name)));
                count++;
                if (count >= MaxExaminedRecipes)
                    break;
            }
            if (ent.Comp.ProvidedRecipes.Count > MaxExaminedRecipes)
            {
                args.PushMarkup(Loc.GetString("blueprint-count-others", ("count", ent.Comp.ProvidedRecipes.Count - MaxExaminedRecipes)));
            }
        }
        if (ent.Comp.Modifiers is { } modifiers)
        {
            var color = modifiers.RareLoc.ToString() switch
            {
                "research-technology-blueprint-uncommon" => "#33FF44",
                "research-technology-blueprint-rare" => "#33CCFF",
                "research-technology-blueprint-epic" => "#CC66FF",
                "research-technology-blueprint-unique" => "#FFCC66",
                _ => "#CCCCCC",
            };
            args.PushMarkup(Loc.GetString("research-blueprint-examine-rarity",
                ("rarity", Loc.GetString(modifiers.RareLoc)), ("color", color)), priority: -1000);
        }
    }
    // End Frontier
}

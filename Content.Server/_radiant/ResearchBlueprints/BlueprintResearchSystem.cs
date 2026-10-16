using System.Linq;
using Content.Server.Administration.Logs;
using Content.Server.Lathe;
using Content.Server.Materials;
using Content.Server.Power.EntitySystems;
using Content.Server.Research.Components;
using Content.Server.Research.Systems;
using Content.Shared._radiant.ResearchBlueprints;
using Content.Shared.Access.Components;
using Content.Shared.Access.Systems;
using Content.Shared.Database;
using Content.Shared.Lathe;
using Content.Shared.Research.Components;
using Content.Shared.Research.Prototypes;
using Content.Shared.UserInterface;
using Robust.Server.GameObjects;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;

namespace Content.Server._radiant.ResearchBlueprints;

public sealed class BlueprintResearchSystem : SharedBlueprintResearchSystem
{
    [Dependency] private readonly IPrototypeManager _proto = default!;
    [Dependency] private readonly IRobustRandom _random = default!;
    [Dependency] private readonly ResearchSystem _research = default!;
    [Dependency] private readonly LatheSystem _lathes = default!;
    [Dependency] private readonly UserInterfaceSystem _ui = default!;
    [Dependency] private readonly SharedContainerSystem _containers = default!;
    [Dependency] private readonly AccessReaderSystem _access = default!;
    [Dependency] private readonly MaterialStorageSystem _materials = default!;
    [Dependency] private readonly IAdminLogManager _logs = default!;
    [Dependency] private readonly MetaDataSystem _metadata = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ResearchConsoleComponent, ResearchBlueprintRollMessage>(OnRoll);
        SubscribeLocalEvent<LatheComponent, ComponentInit>(OnLatheInit);
        SubscribeLocalEvent<BlueprintLibraryComponent, BlueprintLibrarySelectMessage>(OnSelect);
        SubscribeLocalEvent<BlueprintLibraryComponent, BlueprintLibraryEjectMessage>(OnEject);
        SubscribeLocalEvent<BlueprintLibraryComponent, BeforeActivatableUIOpenEvent>((uid, _, _) => UpdateLibrary(uid));
        SubscribeLocalEvent<BlueprintLibraryComponent, EntInsertedIntoContainerMessage>((uid, _, _) => UpdateLibrary(uid));
        SubscribeLocalEvent<BlueprintLibraryComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<BlueprintLibraryComponent, ComponentShutdown>(OnLibraryShutdown);
    }

    private void OnLatheInit(EntityUid uid, LatheComponent comp, ComponentInit args)
    {
        if (comp.LatheBlueprint != null)
            EnsureComp<ResearchClientComponent>(uid);
    }

    private bool Allowed(EntityUid machine, EntityUid actor)
        => this.IsPowered(machine, EntityManager) &&
           (!TryComp<AccessReaderComponent>(machine, out var access) || _access.IsAllowed(actor, machine, access));

    private void OnRoll(EntityUid uid, ResearchConsoleComponent comp, ResearchBlueprintRollMessage args)
    {
        if (Allowed(uid, args.Actor) && comp.BlueprintResearch)
            TryRoll(uid, comp);
        UpdateConsole(uid, comp);
    }

    public bool TryRoll(EntityUid uid, ResearchConsoleComponent comp)
    {
        if (!this.IsPowered(uid, EntityManager) ||
            !_research.TryGetClientServer(uid, out var serverId, out var server) ||
            !this.IsPowered(serverId.Value, EntityManager) ||
            !_proto.TryIndex(comp.BlueprintResearchPreset, out var settings) || settings.Cost <= 0 || server.Points < settings.Cost)
            return false;
        var technologies = _proto.EnumeratePrototypes<TechnologyBlueprintsPrototype>()
            .Where(t => t.SupportsTier(settings.Tier) && double.IsFinite(t.Rare) && t.Rare > 0 &&
                (t.Recipe.Count > 0 || t.RecipeForce.Count > 0)).OrderBy(t => t.ID).ToArray();
        if (technologies.Length == 0)
            return false;
        var technology = BlueprintResearchRoller.PickWeighted(_random, technologies, t => 1 / t.Rare);
        var recipes = BlueprintResearchRoller.RollRecipes(_random, technology);
        if (recipes.Any(r => !_proto.HasIndex(r)))
            return false;
        var required = recipes.SelectMany(r => _proto.Index(r).Materials.Keys).ToHashSet();
        LatheBlueprintPrototype? group = null;
        if (_proto.Index(technology.Lathe).TryGetComponent<LatheComponent>(out var machineLathe, EntityManager.ComponentFactory) &&
            machineLathe.LatheBlueprint is { } groupId)
            _proto.TryIndex(groupId, out group);
        var modifiers = BlueprintResearchRoller.Roll(_random, technology, settings,
            settings.MaterialTiers.Select(t => _proto.Index(t)).ToArray(), group?.Lathe ?? new List<EntProtoId>(), required);
        var blueprint = Spawn(settings.Blueprint, Transform(uid).Coordinates);
        SetBlueprint((blueprint, EnsureComp<BlueprintComponent>(blueprint)), recipes, modifiers);
        var item = string.Join(", ", recipes.OrderBy(r => r.Id).Select(recipe => _lathes.GetRecipeName(_proto.Index(recipe))));
        _metadata.SetEntityName(blueprint, Loc.GetString("research-blueprint-paper-name", ("item", item)));
        server.BlueprintResults.Insert(0, new BlueprintResearchResult
        {
            InstanceId = modifiers.InstanceId, Technology = technology.ID,
            Recipes = recipes.OrderBy(r => r.Id).ToList(), RareLoc = modifiers.RareLoc,
        });
        if (server.BlueprintResults.Count > 50) server.BlueprintResults.RemoveRange(50, server.BlueprintResults.Count - 50);
        Dirty(serverId.Value, server);
        _research.ModifyServerPoints(serverId.Value, -settings.Cost, server);
        _logs.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(uid)} issued blueprint {modifiers.InstanceId} ({technology.ID}) for {settings.Cost} research points");
        return true;
    }

    public void UpdateConsole(EntityUid uid, ResearchConsoleComponent comp)
    {
        if (!_proto.TryIndex(comp.BlueprintResearchPreset, out var settings)) return;
        var connected = _research.TryGetClientServer(uid, out var serverUid, out var server) && this.IsPowered(serverUid.Value, EntityManager);
        _ui.SetUiState(uid, ResearchConsoleUiKey.Key, new ResearchBlueprintConsoleState(
            connected ? server!.Points : 0, settings.Cost, connected, connected ? new(server!.BlueprintResults) : new()));
    }

    private List<EntityUid> Contents(EntityUid library)
    {
        if (!TryComp<BlueprintReceiverComponent>(library, out var receiver) ||
            !_containers.TryGetContainer(library, receiver.ContainerId, out var container)) return new();
        return container.ContainedEntities.Where(e => HasComp<BlueprintComponent>(e)).ToList();
    }

    private BlueprintModifiers Data(EntityUid blueprint, BlueprintComponent comp)
        => comp.Modifiers?.Copy() ?? new BlueprintModifiers { InstanceId = GetNetEntity(blueprint).ToString() };

    public void UpdateLibrary(EntityUid uid)
    {
        if (TerminatingOrDeleted(uid)) return;
        var connected = _research.TryGetClientServer(uid, out var serverUid, out var server) && this.IsPowered(serverUid.Value, EntityManager);
        var entries = new List<BlueprintLibraryEntry>();
        foreach (var blueprint in Contents(uid))
        {
            var comp = Comp<BlueprintComponent>(blueprint);
            var entry = new BlueprintLibraryEntry(GetNetEntity(blueprint),
                comp.ProvidedRecipes.Where(r => _proto.HasIndex(r)).OrderBy(r => r.Id).ToList(), Data(blueprint, comp));
            if (connected)
                foreach (var client in server!.Clients)
                {
                    if (TerminatingOrDeleted(client) || !TryComp<LatheComponent>(client, out var lathe) ||
                        !_lathes.MatchesBlueprintLathe(client, lathe, entry.Modifiers) || MetaData(client).EntityPrototype is not { } machine)
                        continue;
                    foreach (var recipe in entry.Recipes)
                    {
                        if (entry.Modifiers.Lathe == null && !_lathes.GetAllPossibleRecipes(lathe).Contains(recipe)) continue;
                        if (!entry.Destinations.TryGetValue(recipe, out var destinations))
                            entry.Destinations[recipe] = destinations = new();
                        destinations.Add(new(GetNetEntity(client), MetaData(client).EntityName, machine.ID));
                    }
                }
            entries.Add(entry);
        }
        _ui.SetUiState(uid, BlueprintLibraryUiKey.Key, new BlueprintLibraryState(entries,
            connected ? server!.BlueprintRecipes.ToDictionary(p => p.Key, p => p.Value.InstanceId) : new(), connected)
        {
            ServerName = connected ? server!.ServerName : string.Empty,
            ServerId = connected ? server!.Id : -1,
        });
    }

    private void OnSelect(EntityUid uid, BlueprintLibraryComponent comp, BlueprintLibrarySelectMessage args)
    {
        if (Allowed(uid, args.Actor)) TrySelectBlueprint(uid, GetEntity(args.Blueprint), args.Recipe);
    }

    public bool TrySelectBlueprint(EntityUid uid, EntityUid blueprint, string recipe)
    {
        if (!HasComp<BlueprintLibraryComponent>(uid) || !this.IsPowered(uid, EntityManager) ||
            !Contents(uid).Contains(blueprint) || !TryComp<BlueprintComponent>(blueprint, out var data) || !data.ProvidedRecipes.Contains(recipe) ||
            !_research.TryGetClientServer(uid, out var serverUid, out var server) || !this.IsPowered(serverUid.Value, EntityManager) ||
            !_proto.TryIndex<LatheRecipePrototype>(recipe, out var selected)) return false;
        foreach (var existing in server.BlueprintRecipes.Keys.Where(r => r.Id != recipe &&
                     selected.Result != null && _proto.TryIndex(r, out var existingRecipe) && existingRecipe.Result == selected.Result).ToArray())
        {
            server.BlueprintRecipes.Remove(existing);
            server.BlueprintSources.Remove(existing);
        }
        server.BlueprintRecipes[recipe] = Data(blueprint, data);
        server.BlueprintSources[recipe] = uid;
        Publish(serverUid.Value, server);
        return true;
    }

    private void OnEject(EntityUid uid, BlueprintLibraryComponent comp, BlueprintLibraryEjectMessage args)
    {
        var blueprint = GetEntity(args.Blueprint);
        if (!Allowed(uid, args.Actor) || !Contents(uid).Contains(blueprint) ||
            !TryComp<BlueprintReceiverComponent>(uid, out var receiver)) return;
        _containers.Remove(blueprint, _containers.GetContainer(uid, receiver.ContainerId));
    }

    private void OnRemoved(EntityUid uid, BlueprintLibraryComponent comp, EntRemovedFromContainerMessage args)
    {
        if (_research.TryGetClientServer(uid, out var serverUid, out var server))
        {
            var remaining = Contents(uid).Select(e => Data(e, Comp<BlueprintComponent>(e)).InstanceId).ToHashSet();
            foreach (var recipe in server.BlueprintSources.Where(p => p.Value == uid &&
                !remaining.Contains(server.BlueprintRecipes[p.Key].InstanceId)).Select(p => p.Key).ToArray())
            {
                server.BlueprintSources.Remove(recipe);
                server.BlueprintRecipes.Remove(recipe);
            }
            Publish(serverUid.Value, server);
        }
        UpdateLibrary(uid);
    }

    private void RemoveLibrary(EntityUid uid, EntityUid? serverUid)
    {
        if (!TryComp<ResearchServerComponent>(serverUid, out var server)) return;
        foreach (var recipe in server.BlueprintSources.Where(p => p.Value == uid).Select(p => p.Key).ToArray())
        {
            server.BlueprintSources.Remove(recipe);
            server.BlueprintRecipes.Remove(recipe);
        }
        Publish(serverUid!.Value, server);
    }

    private void OnLibraryShutdown(EntityUid uid, BlueprintLibraryComponent comp, ComponentShutdown args)
        => RemoveLibrary(uid, comp.LastServer);

    public void OnRegistration(EntityUid uid, ResearchClientComponent comp, ref ResearchRegistrationChangedEvent args)
    {
        if (TryComp<BlueprintLibraryComponent>(uid, out var library))
        {
            if (library.LastServer != args.Server) RemoveLibrary(uid, library.LastServer);
            library.LastServer = args.Server;
            UpdateLibrary(uid);
        }
        if (TryComp<LatheComponent>(uid, out var lathe) && lathe.LatheBlueprint != null)
        {
            SynchronizeLathe(uid, lathe, TryComp<ResearchServerComponent>(args.Server, out var server) ? server : null);
            var libraries = EntityQueryEnumerator<BlueprintLibraryComponent>();
            while (libraries.MoveNext(out var libraryUid, out _)) UpdateLibrary(libraryUid);
        }
    }

    private void Publish(EntityUid uid, ResearchServerComponent server)
    {
        if (TerminatingOrDeleted(uid)) return;
        Dirty(uid, server);
        foreach (var client in server.Clients.ToArray())
        {
            if (TerminatingOrDeleted(client)) continue;
            if (TryComp<LatheComponent>(client, out var lathe) && lathe.LatheBlueprint != null) SynchronizeLathe(client, lathe, server);
            if (HasComp<BlueprintLibraryComponent>(client)) UpdateLibrary(client);
        }
    }

    private void SynchronizeLathe(EntityUid uid, LatheComponent lathe, ResearchServerComponent? server)
    {
        lathe.BlueprintRecipes.Clear();
        if (server != null)
            foreach (var (recipe, modifiers) in server.BlueprintRecipes)
                if (_lathes.MatchesBlueprintLathe(uid, lathe, modifiers) &&
                    (modifiers.Lathe != null || _lathes.GetAllPossibleRecipes(lathe).Contains(recipe)))
                    lathe.BlueprintRecipes[recipe] = modifiers.Copy();
        Dirty(uid, lathe);
        _materials.UpdateMaterialWhitelist(uid);
        _lathes.UpdateUserInterfaceState(uid, lathe);
    }
}

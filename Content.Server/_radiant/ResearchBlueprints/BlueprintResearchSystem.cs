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
using Robust.Server.Audio;
using Robust.Shared.Containers;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using System;
using System.Collections.Generic;

namespace Content.Server._radiant.ResearchBlueprints;

public sealed partial class BlueprintResearchSystem : SharedBlueprintResearchSystem
{
    [Dependency] private IPrototypeManager _proto = default!;
    [Dependency] private IRobustRandom _random = default!;
    [Dependency] private ResearchSystem _research = default!;
    [Dependency] private LatheSystem _lathes = default!;
    [Dependency] private UserInterfaceSystem _ui = default!;
    [Dependency] private SharedContainerSystem _containers = default!;
    [Dependency] private AccessReaderSystem _access = default!;
    [Dependency] private MaterialStorageSystem _materials = default!;
    [Dependency] private IAdminLogManager _logs = default!;
    [Dependency] private MetaDataSystem _metadata = default!;
    [Dependency] private AudioSystem _audio = default!;
    [Dependency] private IGameTiming _timing = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<ResearchConsoleComponent, ResearchBlueprintRollMessage>(OnRoll);
        SubscribeLocalEvent<BlueprintComponent, MapInitEvent>(OnBlueprintMapInit);
        SubscribeLocalEvent<BlueprintResearchSpawnerComponent, MapInitEvent>(OnResearchSpawnerMapInit);
        SubscribeLocalEvent<LatheComponent, ComponentInit>(OnLatheInit);
        SubscribeLocalEvent<BlueprintLibraryComponent, BlueprintLibrarySelectMessage>(OnSelect);
        SubscribeLocalEvent<BlueprintLibraryComponent, BlueprintLibraryUnselectMessage>(OnUnselect);
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
           (!TryComp<Content.Server._radiant.Machines.PersistentMachineComponent>(machine, out var storage) || storage.Available) &&
           (!TryComp<AccessReaderComponent>(machine, out var access) || _access.IsAllowed(actor, machine, access));

    private void OnRoll(
        EntityUid uid,
        ResearchConsoleComponent comp,
        ResearchBlueprintRollMessage args)
    {
        if (HasComp<BlueprintResearchPrintingComponent>(uid))
            return;

        if (!Allowed(uid, args.Actor) ||
            !comp.BlueprintResearch ||
            !TryGetRollInfo(uid, comp, out var serverUid, out var server, out var settings) ||
            !TryComp<BlueprintResearchPrinterComponent>(uid, out var printer) ||
            printer.Duration <= TimeSpan.Zero)
        {
            UpdateConsole(uid, comp);
            return;
        }

        var now = _timing.CurTime;

        var printing = EnsureComp<BlueprintResearchPrintingComponent>(uid);
        printing.ServerUid = serverUid;
        printing.StartTime = now;
        printing.Duration = printer.Duration;
        printing.FinishTime = now + printer.Duration;

        printing.PrintSoundTime = now +
            TimeSpan.FromSeconds(Math.Max(0, printer.Duration.TotalSeconds - 2));

        printing.NextUiUpdate = now;
        printing.PrintSoundPlayed = false;

        _research.ModifyServerPoints(serverUid, -settings.Cost, server);

        _audio.PlayPvs(printer.ScanFinishSound, uid);

        UpdateConsole(uid, comp);
    }

    private bool TryGetRollInfo(
    EntityUid uid,
    ResearchConsoleComponent comp,
    out EntityUid serverUid,
    out ResearchServerComponent server,
    out BlueprintResearchPrototype settings)
    {
        serverUid = default;
        server = null!;
        settings = null!;

        if (!this.IsPowered(uid, EntityManager) ||
            !_research.TryGetClientServer(uid, out var linkedServerUid, out var linkedServer) ||
            linkedServerUid is null ||
            linkedServer is null ||
            !this.IsPowered(linkedServerUid.Value, EntityManager) ||
            !_proto.TryIndex(comp.BlueprintResearchPreset, out var preset) ||
            preset.Cost <= 0 ||
            linkedServer.Points < preset.Cost)
            return false;

        var hasTechnologies = _proto
            .EnumeratePrototypes<TechnologyBlueprintsPrototype>()
            .Any(preset.Allows);

        if (!hasTechnologies)
            return false;

        serverUid = linkedServerUid.Value;
        server = linkedServer;
        settings = preset;

        return true;
    }

    public bool TryRoll(EntityUid uid, ResearchConsoleComponent comp)
    {
        if (!this.IsPowered(uid, EntityManager) ||
            !_research.TryGetClientServer(uid, out var serverId, out var server) ||
            serverId is null ||
            server is null ||
            !this.IsPowered(serverId.Value, EntityManager))
            return false;

        return TryRoll(uid, comp, serverId.Value, server, costPaid: false);
    }

    private bool TryRoll(
        EntityUid uid,
        ResearchConsoleComponent comp,
        EntityUid serverUid,
        ResearchServerComponent server,
        bool costPaid)
    {
        if (!_proto.TryIndex(comp.BlueprintResearchPreset, out var settings) ||
            settings.Cost <= 0 ||
            (!costPaid && server.Points < settings.Cost))
            return false;
        if (!TryCreateBlueprint(Transform(uid).Coordinates, settings, out var blueprint, out var technology))
            return false;
        var modifiers = blueprint.Comp.Modifiers!;
        server.BlueprintResults.Insert(0, new BlueprintResearchResult
        {
            InstanceId = modifiers.InstanceId, Technology = technology.ID,
            Recipes = blueprint.Comp.ProvidedRecipes.OrderBy(r => r.Id).ToList(), RareLoc = modifiers.RareLoc,
        });
        if (server.BlueprintResults.Count > 50) server.BlueprintResults.RemoveRange(50, server.BlueprintResults.Count - 50);
        Dirty(serverUid, server);
        if (!costPaid)
            _research.ModifyServerPoints(serverUid, -settings.Cost, server);
        _logs.Add(LogType.Action, LogImpact.Medium, $"{ToPrettyString(uid)} issued blueprint {modifiers.InstanceId} ({technology.ID}) for {settings.Cost} research points");
        return true;
    }

    public void UpdateConsole(EntityUid uid, ResearchConsoleComponent comp)
    {
        if (!_proto.TryIndex(comp.BlueprintResearchPreset, out var settings)) return;
        var connected = _research.TryGetClientServer(uid, out var serverUid, out var server)
                && serverUid is not null
                && server is not null
                && this.IsPowered(serverUid.Value, EntityManager);
        var isPrinting = TryComp<BlueprintResearchPrintingComponent>(uid, out var printing);

        var duration = isPrinting
            ? Math.Max(0.01, printing!.Duration.TotalSeconds)
            : 10.0;

        var remaining = isPrinting
            ? Math.Clamp((printing!.FinishTime - _timing.CurTime).TotalSeconds, 0, duration)
            : 0.0;

        var points = connected ? server!.Points : 0;
        var results = connected
            ? new List<BlueprintResearchResult>(server!.BlueprintResults)
            : new List<BlueprintResearchResult>();

        var state = new ResearchBlueprintConsoleState(
            points,
            settings.Cost,
            connected,
            results)
        {
            Printing = isPrinting,
            PrintRemaining = (float)remaining,
            PrintProgress = isPrinting
                ? (float)Math.Clamp(1.0 - remaining / duration, 0.0, 1.0)
                : 0f
        };

        _ui.SetUiState(uid, ResearchConsoleUiKey.Key, state);
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

    public override void Update(float frameTime)
    {
        base.Update(frameTime);

        var query = EntityQueryEnumerator<
            BlueprintResearchPrintingComponent,
            ResearchConsoleComponent>();

        while (query.MoveNext(out var uid, out var printing, out var console))
        {
            var now = _timing.CurTime;

            if (!printing.PrintSoundPlayed && now >= printing.PrintSoundTime)
            {
                if (TryComp<BlueprintResearchPrinterComponent>(uid, out var printer))
                    _audio.PlayPvs(printer.PrintSound, uid);

                printing.PrintSoundPlayed = true;
            }

            if (now >= printing.FinishTime)
            {
                var serverUid = printing.ServerUid;

                RemComp<BlueprintResearchPrintingComponent>(uid);

                if (!TerminatingOrDeleted(serverUid) &&
                    TryComp<ResearchServerComponent>(serverUid, out var server))
                {
                    TryRoll(uid, console, serverUid, server, costPaid: true);
                }

                UpdateConsole(uid, console);
                continue;
            }

            if (now >= printing.NextUiUpdate)
            {
                printing.NextUiUpdate = now + TimeSpan.FromMilliseconds(100);
                UpdateConsole(uid, console);
            }
        }
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

    private void OnUnselect(
    EntityUid uid,
    BlueprintLibraryComponent comp,
    BlueprintLibraryUnselectMessage args)
    {
        if (!Allowed(uid, args.Actor))
            return;

        TryUnselectBlueprint(uid, GetEntity(args.Blueprint), args.Recipe);
    }

    public bool TryUnselectBlueprint(
        EntityUid uid,
        EntityUid blueprint,
        string recipe)
    {
        if (!HasComp<BlueprintLibraryComponent>(uid) ||
            !this.IsPowered(uid, EntityManager) ||
            !Contents(uid).Contains(blueprint) ||
            !TryComp<BlueprintComponent>(blueprint, out var data) ||
            !data.ProvidedRecipes.Contains(recipe) ||
            !_research.TryGetClientServer(uid, out var serverUid, out var server) ||
            serverUid is null ||
            server is null ||
            !this.IsPowered(serverUid.Value, EntityManager))
        {
            return false;
        }

        if (!server.BlueprintSources.TryGetValue(recipe, out var source) ||
            source != uid)
        {
            return false;
        }

        if (!server.BlueprintRecipes.TryGetValue(recipe, out var assigned) ||
            assigned.InstanceId != Data(blueprint, data).InstanceId)
        {
            return false;
        }

        if (!server.BlueprintRecipes.Remove(recipe))
            return false;

        server.BlueprintSources.Remove(recipe);

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

using System;
using System.IO;
using System.Linq;
using System.Numerics;
using Content.Server._HL.Shipyard;
using Content.Server._radiant.Careers;
using Content.Server._NF.Station.Components;
using Content.Server._NF.Shipyard.Components;
using Content.Server.StationEvents.Components;
using Content.Server.StationRecords;
using Content.Server.StationRecords.Components;
using Content.Shared.StationRecords;
using Content.Server._Triad.Shipyard;
using Content.Server.Database;
using Content.Server.Maps;
using Content.Server.Shuttles.Components;
using Content.Shared._NF.Bank.Components;
using Content.Shared._NF.Shipyard;
using Content.Shared._NF.Shipyard.Components;
using Content.Shared._NF.Shipyard.Events;
using Content.Shared._NF.Shipyard.Prototypes;
using Content.Shared._NF.ShuttleRecords;
using Content.Shared._Triad.Shipyard.Save;
using Content.Shared._radiant.Shipyard;
using Content.Shared.Access.Components;
using Content.Shared.Database;
using Content.Shared.Mobs.Components;
using Content.Shared.Shuttles.Components;
using Content.Shared.Station.Components;
using Content.Shared.Station;
using Content.Shared.Whitelist;
using Robust.Shared.Configuration;
using Robust.Shared.Map.Components;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;

namespace Content.Server._NF.Shipyard.Systems;

public sealed partial class ShipyardSystem
{
    [Dependency] private readonly ShipyardGridSaveSystem _gridSave = default!;
    [Dependency] private readonly ShipSaveSigningSystem _shipSigning = default!;
    [Dependency] private readonly TriadTamperPolicyService _tamperPolicy = default!;
    [Dependency] private readonly EntityWhitelistSystem _saveWhitelist = default!;

    private void InitializeShipSaving()
    {
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleSaveMessage>(OnSaveShip);
        SubscribeLocalEvent<ShipyardConsoleComponent, ShipyardConsoleLoadMessage>(OnLoadShip);
    }

    private bool TryGetSaveContext(EntityUid uid, ShipyardConsoleComponent console, EntityUid player,
        out EntityUid id, out ICommonSession session)
    {
        id = default;
        session = default!;
        if (!_enabled || !console.AllowShipSaving || !_configManager.GetCVar(ShipSaveCVars.Enabled)
            || !_saveWhitelist.CheckBoth(player, console.ShipSaveBlacklist, console.ShipSaveWhitelist)
            || HasComp<ShipSavingBlacklistComponent>(player))
            return false;

        if (console.TargetIdSlot.ContainerSlot?.ContainedEntity is not { } targetId
            || !HasComp<IdCardComponent>(targetId) || HasComp<ShipyardVoucherComponent>(targetId))
        {
            SaveError(uid, console, player, "shipyard-console-invalid-idcard");
            return false;
        }

        if (TryComp<AccessReaderComponent>(uid, out var access) && !_access.IsAllowed(player, uid, access))
        {
            SaveError(uid, console, player, "comms-console-permission-denied");
            return false;
        }

        if (!_player.TryGetSessionByEntity(player, out var foundSession))
            return false;

        id = targetId;
        session = foundSession;
        return true;
    }

    public bool CanSaveVessel(EntityUid grid)
        => TryComp<SavedShipComponent>(grid, out var saved) && saved.VesselId is { } vesselId
            && _prototypeManager.TryIndex<VesselPrototype>(vesselId, out var vessel) && vessel.CanSave;

    private void SaveError(EntityUid uid, ShipyardConsoleComponent console, EntityUid player, string locId)
    {
        ConsolePopup(player, Loc.GetString(locId));
        PlayDenySound(player, uid, console);
    }

    private void OnSaveShip(EntityUid uid, ShipyardConsoleComponent console, ShipyardConsoleSaveMessage args)
    {
        var player = args.Actor;
        if (!TryGetSaveContext(uid, console, player, out var id, out var session))
            return;

        if (!TryComp<ShuttleDeedComponent>(id, out var deed)
            || deed.ShuttleUid is not { } grid || Deleted(grid) || EntityManager.IsQueuedForDeletion(grid)
            || !HasComp<MapGridComponent>(grid) || !HasComp<ShuttleComponent>(grid))
        {
            SaveError(uid, console, player, "shipyard-console-no-deed");
            return;
        }

        if (!CanSaveVessel(grid))
        {
            SaveError(uid, console, player, "shipyard-save-vessel-disabled");
            return;
        }

        if (deed.PurchasedWithVoucher || HasComp<ShipSavingBlacklistComponent>(grid))
        {
            SaveError(uid, console, player, "shipyard-save-not-allowed");
            return;
        }

        // Storage must happen at the console's own dock, as with ship sales.
        var consoleGrid = Transform(uid).GridUid;
        if (consoleGrid == null || consoleGrid == grid
            || !_docking.GetDocks(grid).Any(dock => dock.Comp.DockedWith is { } other
                && Transform(other).GridUid == consoleGrid))
        {
            SaveError(uid, console, player, "shipyard-console-sale-not-docked");
            return;
        }

        var organic = FoundOrganics(grid, GetEntityQuery<MobStateComponent>(), GetEntityQuery<TransformComponent>());
        if (organic != null)
        {
            ConsolePopup(player, Loc.GetString("shipyard-console-sale-organic-aboard", ("name", organic)));
            PlayDenySound(player, uid, console);
            return;
        }

        var blocked = FindDisableShipyardSaleObjects(grid, (ShipyardConsoleUiKey)args.UiKey,
            GetEntityQuery<ShipyardSellConditionComponent>(), GetEntityQuery<TransformComponent>());
        if (blocked != null)
        {
            SaveError(uid, console, player, blocked);
            return;
        }

        if (!_gridSave.CheckGridEntityLimits(grid, out var limitMessage))
        {
            ConsolePopup(player, limitMessage);
            PlayDenySound(player, uid, console);
            return;
        }

        var name = GetFullName(deed);
        if (!_gridSave.TrySaveShip(grid, id, session, success =>
        {
            if (Deleted(uid) || Deleted(player) || Deleted(id))
                return;
            if (!success)
            {
                SaveError(uid, console, player, "shipyard-save-failed");
                return;
            }
            _records.UpdateRegisteredShuttleFromId(id);
            ConsolePopup(player, Loc.GetString("shipyard-save-success", ("ship", name)));
            PlayConfirmSound(player, uid, console);
            _adminLogger.Add(LogType.ShipYardUsage, LogImpact.Low,
                $"{ToPrettyString(player):actor} saved shuttle {name} via {ToPrettyString(uid)}");
            RefreshState(uid, CompOrNull<BankAccountComponent>(player)?.Balance ?? 0, true, null, 0,
                id, (ShipyardConsoleUiKey)args.UiKey, false);
        }))
        {
            SaveError(uid, console, player, "shipyard-save-failed");
            return;
        }

    }

    private void OnLoadShip(EntityUid uid, ShipyardConsoleComponent console, ShipyardConsoleLoadMessage args)
    {
        var player = args.Actor;
        if (!TryGetSaveContext(uid, console, player, out var id, out var session))
            return;

        if (TryComp<ShuttleDeedComponent>(id, out var oldDeed))
        {
            if (oldDeed.ShuttleUid is { } oldGrid && !Deleted(oldGrid))
            {
                SaveError(uid, console, player, "shipyard-console-already-deeded");
                return;
            }
            RemComp<ShuttleDeedComponent>(id);
        }

        if (!TryComp<BankAccountComponent>(player, out var bank))
        {
            SaveError(uid, console, player, "shipyard-console-no-bank");
            return;
        }

        if (_station.GetOwningStation(uid) is not { } station || Transform(uid).GridUid is not { } dockGrid)
        {
            SaveError(uid, console, player, "shipyard-console-invalid-station");
            return;
        }

        EntityUid? loadedGrid = null;
        EntityUid? loadedStation = null;
        var charged = 0;
        var completed = false;
        AuthenticatedShipFile? auditFile = null;
        var auditRecorded = false;
        string? auditName = null;
        try
        {
            // Load only the supplied payload, never a client-supplied server filesystem path.
            if (args.YamlData.Length > 32 * 1024 * 1024)
                throw new InvalidDataException("Ship file exceeds the size limit.");
            var file = AuthenticatedShipFile.FromShipFile(args.YamlData);
            auditFile = file;
            auditName = args.SourceFilePath is { } path ? Path.GetFileNameWithoutExtension(path) : null;
            var decision = _tamperPolicy.EvaluateLoad(file, session.UserId, auditName);
            if (!decision.Allow)
            {
                _tamperPolicy.RecordLoad(file, session, auditName, decision.ResolvedEvent, null, args.SourceFilePath);
                auditRecorded = true;
                SaveError(uid, console, player, decision.PopupReasonLocId ?? "shipyard-load-invalid-signature");
                return;
            }
            // An exception may import an unknown foreign/legacy file, never duplicate an already
            // deployed or revoked snapshot. Trusted local files still require a confirmed save.
            if (!_shipSigning.IsAvailable(file)
                && (_shipSigning.IsKnown(file) || _shipSigning.IsTrusted(file)))
            {
                SaveError(uid, console, player, "shipyard-load-unavailable");
                return;
            }

            var yaml = ShipSaveYamlSanitizer.ScrubShipLoadYaml(file.ShipYamlString(), out _);
            SetupShipyardIfNeeded();
            using var reader = new StringReader(yaml);
            if (ShipyardMap == null || !_mapLoader.TryLoadGrid(ShipyardMap.Value, reader, "saved ship",
                    out var gridEntity, offset: new Vector2(500f + _shuttleIndex, 1f)))
                throw new InvalidDataException("Unable to load ship grid.");

            var grid = gridEntity.Value.Owner;
            loadedGrid = grid;
            _shuttleIndex += gridEntity.Value.Comp.LocalAABB.Width + ShuttleSpawnBuffer;
            if (!TryComp<ShuttleComponent>(grid, out var shuttle))
                throw new InvalidDataException("Saved grid is not a shuttle.");

            if (!TryComp<SavedShipComponent>(grid, out var ownership) || !ownership.HasOwner)
            {
                SaveError(uid, console, player, "shipyard-load-owner-missing");
                return;
            }
            if (!ownership.IsOwner(session.UserId, _gridSave.GetOwnerCharacterName(player)))
            {
                SaveError(uid, console, player, "shipyard-load-owner-mismatch");
                return;
            }

            VesselPrototype? careerVessel = null;
            if (ownership.VesselId is { } savedVesselId)
                _prototypeManager.TryIndex(savedVesselId, out careerVessel);
            if (careerVessel?.CanSave != true)
            {
                SaveError(uid, console, player, "shipyard-save-vessel-disabled");
                return;
            }
            if ((HasComp<CareerFleetGridComponent>(grid) && careerVessel?.CareerProfession == null)
                || (careerVessel == null && EntityManager.System<CareerSystem>().GetProfession(session) != "Adventurer")
                || careerVessel?.CareerFree == true)
            {
                SaveError(uid, console, player, "career-fleet-save-unavailable");
                return;
            }
            if (careerVessel != null && !_careerFleet.CanPurchase(session, careerVessel, out var careerReason,
                    allowReplacement: false))
            {
                ConsolePopup(player, careerReason!);
                PlayDenySound(player, uid, console);
                return;
            }
            _gridSave.ClearLoadedLatheMaterials(grid);

            var cost = Math.Max(0, checked((int)MathF.Round((float)_pricing.AppraiseGrid(grid, null)
                * Math.Clamp(_configManager.GetCVar(ShipSaveCVars.LoadShipPrice), 0f, 1f))));
            if (!_bank.TryBankWithdraw(player, cost))
            {
                ConsolePopup(player, Loc.GetString("cargo-console-insufficient-funds", ("cost", cost)));
                PlayDenySound(player, uid, console);
                return;
            }
            charged = cost;

            _gridSave.CreateSpawnOnShipLoadEntities(grid);
            RestoreSavedShipIFF(grid);
            var name = Name(grid);
            // The original vessel ID is stored separately so a renamed ship still gets a station.
            if (TryComp<SavedShipComponent>(grid, out var saved)
                && saved.VesselId is { } vesselId
                && _prototypeManager.TryIndex<GameMapPrototype>(vesselId, out var mapProto)
                && mapProto.Stations.TryGetValue(vesselId, out var stationConfig))
            {
                loadedStation = _station.InitializeNewStation(stationConfig, new[] { grid }, name);
                EnsureComp<ExtraShuttleInformationComponent>(loadedStation.Value).Vessel = vesselId;
            }
            else
                loadedStation = _station.InitializeNewStation(new StationConfig
                {
                    StationPrototype = "StandardFrontierVessel",
                    StationComponentOverrides = new()
                }, new[] { grid }, name);

            if (TryComp<AccessComponent>(id, out var access))
                _accessSystem.TrySetTags(id, access.Tags.Concat(console.NewAccessLevels).Distinct().ToList(), access);

            var ownerName = Name(player).Trim();
            var deed = EnsureComp<ShuttleDeedComponent>(id);
            AssignShuttleDeedProperties((id, deed), grid, name, ownerName, false);
            deed.DeedHolder = id;
            deed.LoadedFromSave = true;
            Dirty(id, deed);
            var shipDeed = EnsureComp<ShuttleDeedComponent>(grid);
            AssignShuttleDeedProperties((grid, shipDeed), grid, name, ownerName, false);
            shipDeed.LoadedFromSave = true;
            Dirty(grid, shipDeed);

            var consoles = EntityQueryEnumerator<ShuttleConsoleComponent, TransformComponent>();
            while (consoles.MoveNext(out var consoleUid, out _, out var xform))
            {
                if (xform.GridUid == grid)
                    _shuttleConsoleLock.SetShuttleId(consoleUid, grid.ToString(), EnsureComp<ShuttleConsoleLockComponent>(consoleUid));
            }

            if (careerVessel?.CareerProfession != null)
                _careerFleet.Register(loadedStation.Value, grid, player, session, careerVessel, allowReplacement: false);
            else
                _shipOwnership.RegisterShipOwnership(grid, session);
            EnsureComp<LinkedLifecycleGridParentComponent>(grid);
            if (!_shuttle.TryFTLDock(grid, shuttle, dockGrid))
                throw new InvalidOperationException("No available FTL docking destination.");

            _records.UpdateRegisteredShuttleFromId(id);
            if (loadedStation is { } recordStation
                && HasComp<StationRecordsComponent>(recordStation)
                && TryComp<StationRecordKeyStorageComponent>(id, out var recordKey)
                && recordKey.Key is { } key && _records.TryGetRecord<GeneralStationRecord>(key, out var record))
            {
                _records.AddRecordEntry(recordStation, record);
                _records.Synchronize(recordStation);
            }
            _shipSigning.Deploy(file, grid, session, args.SourceFilePath);
            completed = true;
            _tamperPolicy.RecordLoad(file, session, name, decision.ResolvedEvent,
                (int)_pricing.AppraiseGrid(grid, null), args.SourceFilePath);
            auditRecorded = true;
            if (decision.ResolvedEvent == TriadShipyardEventType.LoadMigrated)
            {
                var migrated = _tamperPolicy.ReSignForMigration(file, file.Appraisal ?? 0);
                RaiseNetworkEvent(new MigrateShipFileMessage(args.SourceFilePath ?? string.Empty,
                    migrated.ShipFileString()), session);
            }
            if (console.CanTransferDeed)
                _shuttleRecordsSystem.AddRecord(new ShuttleRecord(deed.ShuttleName ?? name,
                    deed.ShuttleNameSuffix ?? "", ownerName, GetNetEntity(grid), false, (uint)cost,
                    saved?.VesselId ?? "") { LoadedFromSave = true });

            RaiseLocalEvent(new ShipyardShuttleLoadEvent(grid, player));
            ConsolePopup(player, Loc.GetString("shipyard-load-success", ("ship", name), ("cost", cost)));
            PlayConfirmSound(player, uid, console);
            SendPurchaseMessage(uid, player, name, console.ShipyardChannel, false);
            if (console.SecretShipyardChannel is { } secret)
                SendPurchaseMessage(uid, player, name, secret, true);
            var sellValue = CalculateShipResaleValue((uid, console),
                (int)_pricing.AppraiseGrid(grid, LacksPreserveOnSaleComp));
            RefreshState(uid, bank.Balance, true, name, sellValue, id, (ShipyardConsoleUiKey)args.UiKey, false);
            _adminLogger.Add(LogType.ShipYardUsage, LogImpact.Low,
                $"{ToPrettyString(player):actor} loaded saved shuttle {ToPrettyString(grid)} for {cost}");
        }
        catch (Exception exception)
        {
            _sawmill.Error($"Unable to load saved ship: {exception}");
            SaveError(uid, console, player, "shipyard-load-failed");
        }
        finally
        {
            if (!completed && !auditRecorded)
            {
                if (auditFile != null)
                    _tamperPolicy.RecordLoad(auditFile, session, auditName, TriadShipyardEventType.LoadRejected,
                        null, args.SourceFilePath);
                else
                    _ = _tamperPolicy.RecordRejectedLoadAsync(session.UserId, session.Name, args.SourceFilePath,
                        EntityManager.System<Content.Server.GameTicking.GameTicker>().RoundId);
            }
            if (!completed)
            {
                if (charged > 0)
                    _bank.TryBankDeposit(player, charged);
                if (loadedGrid is { } grid)
                {
                    if (TryComp<ShuttleDeedComponent>(id, out var deed) && deed.ShuttleUid == grid)
                        RemComp<ShuttleDeedComponent>(id);
                    if (loadedStation is { } shipStation)
                        _station.DeleteStation(shipStation);
                    QueueDel(grid);
                }
            }
        }
    }

    internal void RestoreSavedShipIFF(EntityUid grid)
    {
        if (HasComp<IFFComponent>(grid))
            return;

        // Older saves stripped IFF. Restore only that component, not all vessel spawn components.
        if (TryComp<SavedShipComponent>(grid, out var saved)
            && saved.VesselId is { } vesselId
            && _prototypeManager.TryIndex<VesselPrototype>(vesselId, out var vessel)
            && vessel.AddComponents.TryGetValue("IFF", out var iff))
        {
            EntityManager.AddComponents(grid, new ComponentRegistry { ["IFF"] = iff });
            return;
        }

        var component = EnsureComp<IFFComponent>(grid);
        _shuttle.SetIFFColor(grid, Color.White, component);
        _shuttle.AddIFFFlag(grid, IFFFlags.IsPlayerShuttle, component);
    }
}

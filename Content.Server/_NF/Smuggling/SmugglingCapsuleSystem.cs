using System;
using System.Collections.Generic;
using System.Text;
using System.Numerics;
using Content.Server.Administration.Logs;
using Content.Server._NF.SectorServices;
using Content.Server._NF.Shipyard.Systems;
using Content.Server._NF.Station.Systems;
using Content.Server.Radio.EntitySystems;
using Content.Server.Shuttles.Components;
using Content.Server.Shuttles.Systems;
using Content.Server.Station.Systems;
using Content.Server.Station.Components;
using Content.Shared._NF.CCVar;
using Content.Shared.Hands.Components;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Paper;
using Content.Shared.Verbs;
using Robust.Shared.Configuration;
using Robust.Shared.Log;
using Robust.Shared.Map;
using Content.Shared.Database;
using Robust.Shared.Map.Components;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;
using Robust.Shared.Utility;
using Robust.Shared.EntitySerialization.Systems;
using Robust.Shared.GameObjects;
using Robust.Server.GameObjects;
using Content.Server._NF.Smuggling.Components;

namespace Content.Server._NF.Smuggling
{
    public sealed class SmugglingCapsuleSystem : EntitySystem
    {
        [Dependency] private readonly IConfigurationManager _cfg = default!;
        [Dependency] private readonly IPrototypeManager _proto = default!;
        [Dependency] private readonly IRobustRandom _random = default!;
        [Dependency] private readonly ShipyardSystem _shipyard = default!;
        [Dependency] private readonly MapLoaderSystem _map = default!;
        [Dependency] private readonly ShuttleSystem _shuttle = default!;
        [Dependency] private readonly SharedMapSystem _mapManager = default!;
        [Dependency] private readonly IGameTiming _timing = default!;
        [Dependency] private readonly RadioSystem _radio = default!;
        [Dependency] private readonly StationSystem _station = default!;
        [Dependency] private readonly IAdminLogManager _adminLogger = default!;
        [Dependency] private readonly SharedHandsSystem _hands = default!;
        [Dependency] private readonly MetaDataSystem _meta = default!;

        private ISawmill _sawmill = default!;
        private int _maxSimultaneousPods = 5;

        public override void Initialize()
        {
            base.Initialize();
            _sawmill = Logger.GetSawmill("smugglingcapsule");

            SubscribeLocalEvent<SmugglingCapsuleItemComponent, GetVerbsEvent<InteractionVerb>>(AddActivateVerb);

            _cfg.OnValueChanged(NFCCVars.SmugglingMaxSimultaneousPods, OnMaxSimultaneousPodsChanged, true);
        }

        private void OnMaxSimultaneousPodsChanged(int value)
        {
            _maxSimultaneousPods = value;
        }

        private void AddActivateVerb(EntityUid uid, SmugglingCapsuleItemComponent comp, GetVerbsEvent<InteractionVerb> args)
        {
            if (!args.CanInteract || !args.CanAccess || args.Hands == null)
                return;

            var verb = new InteractionVerb()
            {
                Text = Loc.GetString("smuggling-capsule-activate-verb", ("name", MetaData(uid).EntityName)),
                Act = () =>
                {
                    if (!EntityManager.TryGetComponent(args.User, out HandsComponent? hands))
                    {
                        TryActivateCapsule(uid, comp, args.User, null);
                        return;
                    }

                    TryActivateCapsule(uid, comp, args.User, hands);
                },
                Priority = 2,
                IconEntity = GetNetEntity(uid)
            };

            args.Verbs.Add(verb);
        }

        private void TryActivateCapsule(EntityUid uid, SmugglingCapsuleItemComponent comp, EntityUid user, HandsComponent? hands)
        {
            _adminLogger?.Add(LogType.Action, LogImpact.Medium,
                 $"{ToPrettyString(user)} activated a smuggling capsule item ({uid}).");

            QueueDel(uid);

            var initialMessage = Loc.GetString(comp.InitialNotifyLoc);

            _radio.SendRadioMessage(uid, initialMessage, comp.ChannelNotify, user);

            var spawnDelaySec = _random.Next(comp.MinSpawnDelaySeconds, comp.MaxSpawnDelaySeconds + 1);

            Timer.Spawn(TimeSpan.FromSeconds(spawnDelaySec), () =>
            {
                SpawnCapsuleGridAndNotify(comp, user);
            });
        }

        private void SpawnCapsuleGridAndNotify(SmugglingCapsuleItemComponent comp, EntityUid user)
        {
            if (_shipyard.ShipyardMap == null)
            {
                _shipyard.SetupShipyardIfNeeded();
                if (_shipyard.ShipyardMap == null)
                {
                    _sawmill.Warning("Shipyard map was not available for smuggling capsule spawn.");
                    return;
                }
            }

            if (!_map.TryLoadGrid(_shipyard.ShipyardMap.Value, comp.DropGrid, out var gridUid))
            {
                _sawmill.Warning($"Failed to load capsule grid {comp.DropGrid} from shipyard.");
                return;
            }

            var grid = gridUid.Value;

            var x = _random.NextFloat(comp.MinimumDistance, comp.MaximumDistance);
            var y = _random.NextFloat(comp.MinimumDistance, comp.MaximumDistance);

            if (_random.Next(0, 2) == 0) x = -x;
            if (_random.Next(0, 2) == 0) y = -y;

            var dropLocation = new Vector2(x, y);

            var mapId = Transform(user).MapID;

            if (!_mapManager.TryGetMap(mapId, out var mapUid))
            {
                _sawmill.Warning("Failed to resolve map for capsule spawn.");
                return;
            }

            var meta = EnsureComp<MetaDataComponent>(grid);
            _meta.SetEntityName(grid, Loc.GetString("smuggling-capsule-grid-name"), meta);

            if (TryComp<ShuttleComponent>(grid, out var shuttle))
            {
                _shuttle.FTLToCoordinates(grid, shuttle, new EntityCoordinates(mapUid.Value, dropLocation), 0f, 0f, 35f);
            }

            var ourGridComp = EnsureComp<OurCapsuleGridComponent>(grid);
            ourGridComp.CreatedAt = _timing.CurTime;

            EnsureComp<ContrabandPodGridComponent>(grid);

            var arrivalMessage = Loc.GetString(comp.ArrivalLoc, ("x", $"{dropLocation.X:F0}"), ("y", $"{dropLocation.Y:F0}"));

            _radio.SendRadioMessage(grid, arrivalMessage, comp.ChannelNotify, user);

            EnforceOwnCapsuleLimit();

            var spawnedGrid = grid;

            Timer.Spawn(TimeSpan.FromMinutes(2), () =>
            {
                var completeMsg = Loc.GetString(comp.CompleteNotifyLoc);

                if (EntityManager.EntityExists(spawnedGrid))
                    _radio.SendRadioMessage(spawnedGrid, completeMsg, comp.ChannelNotifyOpponent, user);
                else
                    _radio.SendRadioMessage(user, completeMsg, comp.ChannelNotifyOpponent, user);
            });
        }

        private bool TryGetSourceGrid(EntityUid user, out EntityUid sourceGrid)
        {
            if (EntityManager.TryGetComponent(user, out TransformComponent? xform) && xform.GridUid != null)
            {
                sourceGrid = xform.GridUid.Value;
                return true;
            }

            sourceGrid = default;
            return false;
        }


        private void EnforceOwnCapsuleLimit()
        {
            var query = EntityManager.EntityQueryEnumerator<OurCapsuleGridComponent>();
            var list = new List<(EntityUid uid, OurCapsuleGridComponent comp)>();
            while (query.MoveNext(out var ent, out var comp))
            {
                list.Add((ent, comp));
            }

            if (list.Count <= _maxSimultaneousPods)
                return;

            list.Sort((a, b) => a.comp.CreatedAt.CompareTo(b.comp.CreatedAt));

            var toRemove = list.Count - _maxSimultaneousPods;
            for (int i = 0; i < toRemove; i++)
            {
                var rem = list[i].uid;
                _adminLogger?.Add(LogType.Action, LogImpact.Medium,
                 $"{rem} scheduled for removal to enforce smuggling capsule limit");
                QueueDel(rem);
            }
        }
    }

    [RegisterComponent]
    public sealed partial class OurCapsuleGridComponent : Component
    {
        [ViewVariables(VVAccess.ReadOnly)]
        public TimeSpan CreatedAt = TimeSpan.Zero;
    }
}

// SPDX-License-Identifier: AGPL-3.0-or-later

using Content.Shared._radiant.Fishing.Components;
using Content.Shared._radiant.Fishing.Events;
using Content.Shared.Actions;
using Content.Shared.Hands.EntitySystems;
using Content.Shared.Popups;
using Content.Shared.Throwing;
using Robust.Shared.Map;
using Robust.Shared.Containers;
using Robust.Shared.Network;
using Robust.Shared.Player;
using Robust.Shared.Prototypes;
using Robust.Shared.Random;
using Robust.Shared.Timing;

namespace Content.Shared._radiant.Fishing.Systems;

/// <summary>
/// This handles... da fish
/// </summary>
public abstract partial class SharedFishingSystem : EntitySystem
{
    [Dependency] protected IGameTiming Timing = default!;
    [Dependency] protected INetManager Net = default!;
    [Dependency] protected ThrowingSystem Throwing = default!;
    [Dependency] protected SharedTransformSystem Xform = default!;
    [Dependency] private SharedActionsSystem _actions = default!;
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedHandsSystem _hands = default!;
    [Dependency] private SharedContainerSystem _containers = default!;

    public const string HookSlot = "fishing_hook";
    public const string LineSlot = "fishing_line";

    protected EntityQuery<ActiveFisherComponent> FisherQuery;
    protected EntityQuery<ActiveFishingSpotComponent> ActiveFishSpotQuery;
    protected EntityQuery<FishingSpotComponent> FishSpotQuery;
    protected EntityQuery<FishingRodComponent> FishRodQuery;
    protected EntityQuery<FishingLureComponent> FishLureQuery;

    public override void Initialize()
    {
        base.Initialize();

        FisherQuery = GetEntityQuery<ActiveFisherComponent>();
        ActiveFishSpotQuery = GetEntityQuery<ActiveFishingSpotComponent>();
        FishSpotQuery = GetEntityQuery<FishingSpotComponent>();
        FishRodQuery = GetEntityQuery<FishingRodComponent>();
        FishLureQuery = GetEntityQuery<FishingLureComponent>();

        SubscribeLocalEvent<FishingRodComponent, MapInitEvent>(OnFishingRodInit);
        SubscribeLocalEvent<FishingRodComponent, GetItemActionsEvent>(OnGetActions);
        SubscribeLocalEvent<FishingRodComponent, ThrowFishingLureActionEvent>(OnThrowFloat);
        SubscribeLocalEvent<FishingRodComponent, PullFishingLureActionEvent>(OnPullFloat);
        SubscribeLocalEvent<FishingRodComponent, EntParentChangedMessage>(OnRodParentChanged);
        SubscribeLocalEvent<FishingRodComponent, ContainerIsRemovingAttemptEvent>(OnTackleRemoving);
        SubscribeLocalEvent<FishingRodComponent, ContainerIsInsertingAttemptEvent>(OnTackleInserting);
        SubscribeLocalEvent<FishingRodComponent, EntRemovedFromContainerMessage>(OnTackleRemoved);

        SubscribeLocalEvent<FishingRodComponent, EntityTerminatingEvent>(OnRodTerminating);
        SubscribeLocalEvent<FishingLureComponent, EntityTerminatingEvent>(OnLureTerminating);
        SubscribeLocalEvent<ActiveFishingSpotComponent, EntityTerminatingEvent>(OnSpotTerminating);
    }

    public override void Update(float frameTime)
    {
        base.Update(frameTime);
        UpdateFishing();
    }

    private void UpdateFishing()
    {
        if (!Timing.IsFirstTimePredicted)
            return;

        var currentTime = Timing.CurTime;
        var activeFishers = EntityQueryEnumerator<ActiveFisherComponent>();
        while (activeFishers.MoveNext(out var fisher, out var fisherComp))
        {
            // Get fishing rod, then float, then spot... ReCurse.
            if (TerminatingOrDeleted(fisherComp.FishingRod) ||
                !FishRodQuery.TryComp(fisherComp.FishingRod, out var fishingRodComp) ||
                TerminatingOrDeleted(fishingRodComp.FishingLure) ||
                !FishLureQuery.TryComp(fishingRodComp.FishingLure, out var fishingFloatComp) ||
                TerminatingOrDeleted(fishingFloatComp.AttachedEntity) ||
                !ActiveFishSpotQuery.TryComp(fishingFloatComp.AttachedEntity, out var activeSpotComp))
                continue;

            var fishRod = fisherComp.FishingRod;
            var fishSpot = fishingFloatComp.AttachedEntity.Value;

            fisherComp.TotalProgress ??= fishingRodComp.StartingProgress;
            fisherComp.NextStruggle ??= Timing.CurTime + TimeSpan.FromSeconds(fishingRodComp.StartingStruggleTime);

            // Fish fighting logic
            CalculateFightingTimings((fisher, fisherComp), activeSpotComp);

            switch (fisherComp.TotalProgress)
            {
                case < 0f:
                    // It's over
                    _popup.PopupEntity(Loc.GetString("fishing-progress-fail"), fisher, fisher);
                    StopFishing((fishRod, fishingRodComp), fisher, strained: true);
                    continue;

                case >= 1f:
                    if (activeSpotComp.Fish != null)
                    {
                        ThrowFishReward(activeSpotComp.Fish.Value, fishSpot, fisher);
                        _popup.PopupEntity(Loc.GetString("fishing-progress-success"), fisher, fisher);
                    }

                    StopFishing((fishRod, fishingRodComp), fisher);
                    break;
            }
        }

        var fishingSpots = EntityQueryEnumerator<ActiveFishingSpotComponent>();
        while (fishingSpots.MoveNext(out var activeSpotComp))
        {
            if (currentTime < activeSpotComp.FishingStartTime || activeSpotComp.IsActive || activeSpotComp.FishingStartTime == null)
                continue;

            // Trigger start of the fishing process
            if (TerminatingOrDeleted(activeSpotComp.AttachedFishingLure))
                continue;

            // Get fishing lure, then rod, then player... ReCurse.
            if (!FishLureQuery.TryComp(activeSpotComp.AttachedFishingLure, out var fishingFloatComp) ||
                TerminatingOrDeleted(fishingFloatComp.FishingRod) ||
                !FishRodQuery.TryComp(fishingFloatComp.FishingRod, out var fishRodComp))
                continue;

            var fishRod = fishingFloatComp.FishingRod;

            if (TerminatingOrDeleted(fishingFloatComp.FishingRod))
                continue;

            var fisher = Transform(fishingFloatComp.FishingRod).ParentUid;

            if (!Exists(fisher) || TerminatingOrDeleted(fisher))
                continue;

            var activeFisher = EnsureComp<ActiveFisherComponent>(fisher);
            activeFisher.FishingRod = fishRod;
            activeFisher.TotalProgress = fishRodComp.StartingProgress;
            activeFisher.NextStruggle = Timing.CurTime + TimeSpan.FromSeconds(fishRodComp.StartingStruggleTime); // Compensate ping for 0.3 seconds

            // Predicted because it works like 99.9% of the time anyway.
            _popup.PopupPredicted(Loc.GetString("fishing-progress-start"), fisher, fisher);
            activeSpotComp.IsActive = true;
        }

        var fishingLures = EntityQueryEnumerator<FishingLureComponent, TransformComponent>();
        while (fishingLures.MoveNext(out var fishingLure, out var lureComp, out var xform))
        {
            if (lureComp.NextUpdate > Timing.CurTime)
                continue;

            lureComp.NextUpdate = Timing.CurTime + TimeSpan.FromSeconds(lureComp.UpdateInterval);

            if (TerminatingOrDeleted(lureComp.FishingRod) ||
                !FishRodQuery.TryComp(lureComp.FishingRod, out var fishingRodComp))
                continue;

            var lurePos = Xform.GetMapCoordinates(fishingLure, xform);
            var rodPos = Xform.GetMapCoordinates(lureComp.FishingRod);
            var distance = lurePos.Position - rodPos.Position;
            var fisher = Transform(lureComp.FishingRod).ParentUid;

            if (!Exists(fisher) || TerminatingOrDeleted(fisher) ||
                distance.Length() > fishingRodComp.BreakOnDistance ||
                lurePos.MapId != rodPos.MapId ||
                !_hands.IsHolding(fisher, lureComp.FishingRod) ||
                !HasComp<ActorComponent>(fisher))
            {
                var rod = (lureComp.FishingRod, fishingRodComp);
                StopFishing(rod, fisher, strained: distance.Length() > fishingRodComp.BreakOnDistance);
                ToggleFishingActions(rod, fisher, false);
            }
        }
    }

    /// <summary>
    /// if AddPulling is true, we ADD Pulling action and REMOVE Throwing action.
    /// Basically true if we start, and false if we end.
    /// </summary>
    private void ToggleFishingActions(Entity<FishingRodComponent> ent, EntityUid fisher, bool addPulling)
    {
        if (TerminatingOrDeleted(ent) || !Exists(fisher) || TerminatingOrDeleted(fisher))
            return;

        if (addPulling)
        {
            _actions.RemoveAction(ent.Comp.ThrowLureActionEntity);
            _actions.AddAction(fisher, ref ent.Comp.PullLureActionEntity, ent.Comp.PullLureActionId, ent);
        }
        else
        {
            _actions.RemoveAction(ent.Comp.PullLureActionEntity);
            _actions.AddAction(fisher, ref ent.Comp.ThrowLureActionEntity, ent.Comp.ThrowLureActionId, ent);
        }
    }

    protected abstract void CalculateFightingTimings(Entity<ActiveFisherComponent> fisher, ActiveFishingSpotComponent activeSpotComp);

    /// <summary>
    /// Server-side only, sets up fishing float and throws it
    /// </summary>
    protected abstract void SetupFishingFloat(Entity<FishingRodComponent> fishingRod, EntityUid player, EntityCoordinates target);

    /// <summary>
    /// Server-side only, spawns a fish and throws it to our player!
    /// </summary>
    protected abstract void ThrowFishReward(EntProtoId fishId, EntityUid fishSpot, EntityUid target);

    protected virtual void WearTackle(Entity<FishingRodComponent> rod, EntityUid? fisher, bool strained) {}

    public EntityUid? GetTackle(EntityUid rod, string slot) =>
        _containers.TryGetContainer(rod, slot, out var container) && container.ContainedEntities.Count > 0
            ? container.ContainedEntities[0]
            : null;

    public bool HasUsableTackle(EntityUid rod) =>
        TryComp<FishingHookComponent>(GetTackle(rod, HookSlot), out _) &&
        TryComp<FishingLineComponent>(GetTackle(rod, LineSlot), out var line) && line.Durability > 0;

    /// <summary>
    /// Reels the fishing rod back and stops fishing progress if arguments are passed to it.
    /// </summary>
    private void StopFishing(
        Entity<FishingRodComponent> fishingRod,
        EntityUid? fisher,
        bool strained = false)
    {
        var nullOrDeleted =
            fishingRod.Comp.FishingLure == null || TerminatingOrDeleted(fishingRod.Comp.FishingLure.Value);

        if (!nullOrDeleted && FishLureQuery.TryComp(fishingRod.Comp.FishingLure, out var lureComp) &&
            !TerminatingOrDeleted(lureComp.AttachedEntity) &&
            ActiveFishSpotQuery.TryComp(lureComp.AttachedEntity, out var activeSpotComp))
            RemCompDeferred(lureComp.AttachedEntity.Value, activeSpotComp);

        if (!nullOrDeleted && Net.IsServer)
            QueueDel(fishingRod.Comp.FishingLure);

        if (Exists(fisher) && !TerminatingOrDeleted(fisher) && FisherQuery.TryComp(fisher, out var fisherComp))
        {
            RemCompDeferred(fisher.Value, fisherComp);

            ToggleFishingActions(fishingRod, fisher.Value, false);
        }

        fishingRod.Comp.FishingLure = null;
        if (Net.IsServer && !nullOrDeleted && !TerminatingOrDeleted(fishingRod))
        {
            // Charge once per actual cast, never for picking up or moving an idle rod.
            WearTackle(fishingRod, fisher, strained);
            fishingRod.Comp.LoadedBait = FishingBaitKind.None;
            Dirty(fishingRod.Owner, fishingRod.Comp);
        }
    }

    #region Terminating Events

    private void OnRodTerminating(Entity<FishingRodComponent> ent, ref EntityTerminatingEvent args)
    {
        TryStopFishing(ent);
    }

    private void OnLureTerminating(Entity<FishingLureComponent> ent, ref EntityTerminatingEvent args)
    {
        TryStopFishing(ent);
    }

    private void OnSpotTerminating(Entity<ActiveFishingSpotComponent> ent, ref EntityTerminatingEvent args)
    {
        TryStopFishing(ent);
    }

    #endregion

    #region Deletion Helpers

    /// <summary>
    /// Stops fishing by taking only the Fishing rod as an argument.
    /// </summary>
    private void TryStopFishing(Entity<FishingRodComponent> rod)
    {
        var player = Transform(rod).ParentUid;
        StopFishing(rod, player);
    }

    /// <summary>
    /// Stops fishing by taking only the Fishing lure as an argument.
    /// </summary>
    private void TryStopFishing(Entity<FishingLureComponent> lure)
    {
        // A deferred deletion from an earlier cast must not cancel the next cast.
        if (!FishRodQuery.TryComp(lure.Comp.FishingRod, out var rodComp) || rodComp.FishingLure != lure.Owner)
            return;

        TryStopFishing((lure.Comp.FishingRod, rodComp));
    }

    /// <summary>
    /// Stops fishing by taking only the Active spot as an argument.
    /// </summary>
    private void TryStopFishing(Entity<ActiveFishingSpotComponent> spot)
    {
        if (!FishLureQuery.TryComp(spot.Comp.AttachedFishingLure, out var lureComp))
            return;

        if (!FishRodQuery.TryComp(lureComp.FishingRod, out var rodComp) ||
            rodComp.FishingLure != spot.Comp.AttachedFishingLure)
            return;

        TryStopFishing((lureComp.FishingRod, rodComp));
    }

    #endregion

    #region Event Handling

    private void OnTackleRemoving(Entity<FishingRodComponent> ent, ref ContainerIsRemovingAttemptEvent args)
    {
        if (ent.Comp.FishingLure != null && args.Container.ID is HookSlot or LineSlot)
            args.Cancel();
    }

    private void OnTackleInserting(Entity<FishingRodComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (ent.Comp.FishingLure != null && args.Container.ID is HookSlot or LineSlot)
            args.Cancel();
    }

    private void OnTackleRemoved(Entity<FishingRodComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID != HookSlot || TerminatingOrDeleted(ent))
            return;

        ent.Comp.LoadedBait = FishingBaitKind.None;
        Dirty(ent);
    }

    private void OnThrowFloat(Entity<FishingRodComponent> ent, ref ThrowFishingLureActionEvent args)
    {
        if (args.Handled || !Timing.IsFirstTimePredicted)
            return;

        var player = args.Performer;

        if (!_hands.IsHolding(player, ent.Owner))
            return;

        if (!HasUsableTackle(ent))
        {
            _popup.PopupPredicted(Loc.GetString("fishing-tackle-missing"), ent, player);
            args.Handled = true;
            return;
        }

        if (ent.Comp.FishingLure != null || !Xform.IsValid(args.Target))
        {
            args.Handled = true;
            return;
        }

        SetupFishingFloat(ent, player, args.Target);
        ToggleFishingActions(ent, player, true);
        args.Handled = true;
    }

    private void OnPullFloat(Entity<FishingRodComponent> ent, ref PullFishingLureActionEvent args)
    {
        if (args.Handled || !Timing.IsFirstTimePredicted)
            return;

        var player = args.Performer;
        var (uid, component) = ent;

        if (component.FishingLure == null)
        {
            ToggleFishingActions(ent, player, true);
            args.Handled = true;
            return;
        }

        _popup.PopupPredicted(Loc.GetString("fishing-rod-remove-lure", ("ent", Name(uid))), uid, uid);

        if (!FishLureQuery.TryComp(component.FishingLure, out var lureComp))
            return;

        if (lureComp.AttachedEntity != null && Exists(lureComp.AttachedEntity))
        {
            // TODO: so this kinda just lets you pull anything right up to you, it should instead just apply an impulse in your direction modfiied by the weight of the player vs the object
            // Also we need to autoreel/snap the line if the player gets too far away
            // Also we should probably PVS override the lure if the rod is in PVS, and vice versa to stop the joint visuals from popping in/out
            var attachedEnt = lureComp.AttachedEntity.Value;
            var targetCoords = Xform.GetMapCoordinates(Transform(attachedEnt));
            var playerCoords = Xform.GetMapCoordinates(Transform(player));
            var rand = new System.Random((int) Timing.CurTick.Value); // evil random prediction hack

            // Calculate throw direction
            var direction = (playerCoords.Position - targetCoords.Position) * rand.NextFloat(0.2f, 0.85f);

            // Yeet
            Throwing.TryThrow(attachedEnt, direction, 4f, player);
        }

        StopFishing(ent, player);
        ToggleFishingActions(ent, player, false);
        args.Handled = true;
    }

    private void OnFishingRodInit(Entity<FishingRodComponent> ent, ref MapInitEvent args)
    {
        _actions.AddAction(ent, ref ent.Comp.ThrowLureActionEntity, ent.Comp.ThrowLureActionId);
    }

    private void OnRodParentChanged(Entity<FishingRodComponent> ent, ref EntParentChangedMessage args)
    {
        if (TerminatingOrDeleted(ent) || !Exists(args.Transform.ParentUid))
            return;

        // Anything that is an active fisher should be fine.
        if (!FisherQuery.HasComp(args.Transform.ParentUid))
        {
            StopFishing(ent, args.OldParent);
        }
    }

    private void OnGetActions(Entity<FishingRodComponent> ent, ref GetItemActionsEvent args)
    {
        if (ent.Comp.FishingLure == null)
            args.AddAction(ref ent.Comp.ThrowLureActionEntity, ent.Comp.ThrowLureActionId);
        else
            args.AddAction(ref ent.Comp.PullLureActionEntity, ent.Comp.PullLureActionId);
    }

    #endregion
}

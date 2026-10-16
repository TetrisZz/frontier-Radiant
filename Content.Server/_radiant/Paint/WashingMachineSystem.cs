using Content.Server.Power.EntitySystems;
using Content.Shared._radiant.Paint;
using Content.Shared.Audio;
using Content.Shared.Examine;
using Content.Shared.Item;
using Content.Shared.Mobs.Components;
using Content.Shared.Storage.Components;
using Content.Shared.Storage.EntitySystems;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Utility;
using Robust.Shared.Audio.Systems;

namespace Content.Server._radiant.Paint;

public sealed class WashingMachineSystem : EntitySystem
{
    [Dependency] private readonly PowerReceiverSystem _power = default!;
    [Dependency] private readonly SharedEntityStorageSystem _storage = default!;
    [Dependency] private readonly SharedAppearanceSystem _appearance = default!;
    [Dependency] private readonly SharedAmbientSoundSystem _ambient = default!;
    [Dependency] private readonly WashablePaintSystem _paint = default!;
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<WashingMachineComponent, MapInitEvent>(OnInit);
        SubscribeLocalEvent<WashingMachineComponent, GetVerbsEvent<Verb>>(OnVerbs);
        SubscribeLocalEvent<WashingMachineComponent, StorageOpenAttemptEvent>(OnOpenAttempt);
        SubscribeLocalEvent<WashingMachineComponent, StorageAfterOpenEvent>(OnOpen);
        SubscribeLocalEvent<WashingMachineComponent, StorageAfterCloseEvent>(OnClose);
        SubscribeLocalEvent<WashingMachineComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<WashingMachineComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<WashingMachineComponent, ContainerIsInsertingAttemptEvent>(OnInsertAttempt);
        SubscribeLocalEvent<WashingMachineComponent, ExaminedEvent>(OnExamined);
    }

    public bool TryStart(EntityUid uid, WashingMachineComponent? comp = null)
    {
        if (!Resolve(uid, ref comp) || comp.Remaining > 0 || !_power.IsPowered(uid) || !Transform(uid).Anchored ||
            !TryComp<EntityStorageComponent>(uid, out var storage) || storage.Contents.Count == 0 ||
            !_storage.TryCloseStorage(uid))
            return false;
        comp.Remaining = comp.CycleDuration;
        _audio.PlayPvs(comp.StartSound, uid);
        UpdateVisuals(uid, comp);
        return true;
    }

    private void OnVerbs(Entity<WashingMachineComponent> ent, ref GetVerbsEvent<Verb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        var blockedReason = ent.Comp.Remaining > 0 ? null : GetStartBlockedReason(ent);
        args.Verbs.Add(new Verb
        {
            Text = Loc.GetString(ent.Comp.Remaining > 0 ? "washer-stop" : "washer-start"),
            Icon = new SpriteSpecifier.Texture(new ResPath("/Textures/Interface/VerbIcons/clock.svg.192dpi.png")),
            Act = () =>
            {
                if (ent.Comp.Remaining > 0)
                {
                    ent.Comp.Remaining = 0;
                    UpdateVisuals(ent, ent.Comp);
                }
                else
                    TryStart(ent, ent.Comp);
            },
            Disabled = blockedReason != null,
            Message = blockedReason == null ? null : Loc.GetString(blockedReason),
        });
    }

    private string? GetStartBlockedReason(EntityUid uid)
    {
        if (!Transform(uid).Anchored)
            return "washer-unanchored";
        if (!_power.IsPowered(uid))
            return "washer-no-power";
        if (!TryComp<EntityStorageComponent>(uid, out var storage) || storage.Contents.Count == 0)
            return "washer-empty";
        return null;
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<WashingMachineComponent, EntityStorageComponent>();
        while (query.MoveNext(out var uid, out var comp, out var storage))
        {
            if (comp.Remaining <= 0 || MetaData(uid).EntityPaused)
                continue;
            if (_power.IsPowered(uid) && Transform(uid).Anchored)
            {
                comp.Remaining = Math.Max(0, comp.Remaining - frameTime);
                if (comp.Remaining <= 0)
                {
                    foreach (var item in storage.Contents.ContainedEntities)
                        _paint.Wash(item);
                    _audio.PlayPvs(comp.FinishSound, uid);
                }
            }
            UpdateVisuals(uid, comp);
        }
    }

    private void OnInit(Entity<WashingMachineComponent> ent, ref MapInitEvent args) => UpdateVisuals(ent, ent.Comp);
    private void OnOpen(Entity<WashingMachineComponent> ent, ref StorageAfterOpenEvent args) => UpdateVisuals(ent, ent.Comp);
    private void OnClose(Entity<WashingMachineComponent> ent, ref StorageAfterCloseEvent args) => UpdateVisuals(ent, ent.Comp);
    private void OnInserted(Entity<WashingMachineComponent> ent, ref EntInsertedIntoContainerMessage args) => UpdateVisuals(ent, ent.Comp);
    private void OnRemoved(Entity<WashingMachineComponent> ent, ref EntRemovedFromContainerMessage args) => UpdateVisuals(ent, ent.Comp);

    private void OnOpenAttempt(Entity<WashingMachineComponent> ent, ref StorageOpenAttemptEvent args)
    {
        if (ent.Comp.Remaining > 0)
            args.Cancelled = true;
    }

    private void OnInsertAttempt(Entity<WashingMachineComponent> ent, ref ContainerIsInsertingAttemptEvent args)
    {
        if (args.Container.ID == "entity_storage" &&
            (ent.Comp.Remaining > 0 || !HasComp<ItemComponent>(args.EntityUid) || HasComp<MobStateComponent>(args.EntityUid) ||
                TryComp<EntityStorageComponent>(ent, out var storage) && storage.Contents.Count >= storage.Capacity))
            args.Cancel();
    }

    private void OnExamined(Entity<WashingMachineComponent> ent, ref ExaminedEvent args)
    {
        if (ent.Comp.Remaining > 0)
            args.PushText(Loc.GetString(_power.IsPowered(ent) && Transform(ent).Anchored ? "washer-running" : "washer-paused",
                ("seconds", (int) Math.Ceiling(ent.Comp.Remaining))));
    }

    private void UpdateVisuals(EntityUid uid, WashingMachineComponent comp)
    {
        if (!TryComp<EntityStorageComponent>(uid, out var storage))
            return;
        var running = comp.Remaining > 0 && _power.IsPowered(uid) && Transform(uid).Anchored;
        var full = storage.Contents.Count > 0;
        var state = running ? WashingMachineState.Running : storage.Open
            ? full ? WashingMachineState.OpenLoaded : WashingMachineState.OpenEmpty
            : full ? WashingMachineState.ClosedLoaded : WashingMachineState.ClosedEmpty;
        _appearance.SetData(uid, WashingMachineVisuals.State, state);
        _ambient.SetAmbience(uid, running);
    }
}

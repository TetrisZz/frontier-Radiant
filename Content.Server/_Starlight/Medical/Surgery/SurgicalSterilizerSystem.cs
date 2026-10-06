using Content.Server.Power.Components;
using Content.Shared._Starlight.Medical.Surgery;
using Content.Shared._Starlight.Medical.Surgery.Components;
using Content.Shared.Body.Organ;
using Content.Shared.Body.Part;
using Content.Shared.Examine;
using Content.Shared.Popups;
using Content.Shared.Storage;
using Content.Shared.Verbs;
using Robust.Shared.Containers;
using Robust.Shared.Audio;
using Robust.Shared.Audio.Systems;

namespace Content.Server._Starlight.Medical.Surgery;

[RegisterComponent]
public sealed partial class SurgicalSterilizerComponent : Component
{
    [DataField] public float CycleSeconds = 10;
    [DataField] public SoundSpecifier WorkingSound = new SoundPathSpecifier("/Audio/Machines/spinning.ogg")
    {
        Params = AudioParams.Default.WithVolume(-4),
    };
    public EntityUid? AudioEntity;
    [ViewVariables] public float Remaining;
}

public sealed partial class SurgicalSterilizerSystem : EntitySystem
{
    [Dependency] private SharedPopupSystem _popup = default!;
    [Dependency] private SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<SurgicalSterilizerComponent, GetVerbsEvent<ActivationVerb>>(OnVerb);
        SubscribeLocalEvent<SurgicalSterilizerComponent, ExaminedEvent>(OnExamine);
        SubscribeLocalEvent<SurgicalSterilizerComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<SurgicalSterilizerComponent, EntRemovedFromContainerMessage>(OnRemoved);
        SubscribeLocalEvent<SurgicalSterilizerComponent, ComponentShutdown>(OnShutdown);
    }

    private void OnShutdown(Entity<SurgicalSterilizerComponent> ent, ref ComponentShutdown args)
    {
        _audio.Stop(ent.Comp.AudioEntity);
        ent.Comp.AudioEntity = null;
    }

    private void OnInserted(Entity<SurgicalSterilizerComponent> ent, ref EntInsertedIntoContainerMessage args)
    {
        if (args.Container.ID == StorageComponent.ContainerId)
            Cancel(ent);
    }

    private void OnRemoved(Entity<SurgicalSterilizerComponent> ent, ref EntRemovedFromContainerMessage args)
    {
        if (args.Container.ID == StorageComponent.ContainerId)
            Cancel(ent);
    }

    private bool Powered(EntityUid uid)
        => Transform(uid).Anchored && TryComp<ApcPowerReceiverComponent>(uid, out var power) && power.Powered;

    private void Cancel(Entity<SurgicalSterilizerComponent> ent)
    {
        if (ent.Comp.Remaining <= 0)
            return;
        ent.Comp.Remaining = 0;
        _audio.Stop(ent.Comp.AudioEntity);
        ent.Comp.AudioEntity = null;
        _popup.PopupEntity(Loc.GetString("surgical-sterilizer-cancelled"), ent);
    }

    private void OnExamine(Entity<SurgicalSterilizerComponent> ent, ref ExaminedEvent args)
        => args.PushText(Loc.GetString(ent.Comp.Remaining > 0 ? "surgical-sterilizer-running" : "surgical-sterilizer-idle"));

    private void OnVerb(Entity<SurgicalSterilizerComponent> ent, ref GetVerbsEvent<ActivationVerb> args)
    {
        if (!args.CanAccess || !args.CanInteract)
            return;
        var user = args.User;
        args.Verbs.Add(new ActivationVerb
        {
            Text = Loc.GetString("surgical-sterilizer-start"),
            Act = () =>
            {
                if (TerminatingOrDeleted(ent) || ent.Comp.Remaining > 0)
                    return;
                if (!Powered(ent) || !TryComp<StorageComponent>(ent, out var storage) || storage.Container.ContainedEntities.Count == 0)
                {
                    _popup.PopupEntity(Loc.GetString("surgical-sterilizer-unavailable"), ent, user);
                    return;
                }
                ent.Comp.Remaining = ent.Comp.CycleSeconds;
                ent.Comp.AudioEntity = _audio.PlayPvs(ent.Comp.WorkingSound, ent,
                    ent.Comp.WorkingSound.Params.WithLoop(true))?.Entity;
                _popup.PopupEntity(Loc.GetString("surgical-sterilizer-running"), ent, user);
            },
        });
    }

    public override void Update(float frameTime)
    {
        var query = EntityQueryEnumerator<SurgicalSterilizerComponent, StorageComponent>();
        while (query.MoveNext(out var uid, out var machine, out var storage))
        {
            if (machine.Remaining <= 0 || MetaData(uid).EntityPaused)
                continue;
            if (!Powered(uid))
            {
                Cancel((uid, machine));
                continue;
            }
            machine.Remaining -= frameTime;
            if (machine.Remaining > 0)
                continue;
            _audio.Stop(machine.AudioEntity);
            machine.AudioEntity = null;
            foreach (var item in storage.Container.ContainedEntities)
            {
                // Only the actual loaded instruments, never organs, implants or nested containers.
                if (!HasComp<SurgeryToolComponent>(item) || HasComp<OrganComponent>(item)
                    || HasComp<BodyPartComponent>(item) || HasComp<NonContactSurgicalToolComponent>(item)
                    || HasComp<SurgicalDrapeComponent>(item))
                    continue;
                var state = EnsureComp<SurgicalItemSterilityComponent>(item);
                SurgicalSterilityRules.Disinfect(state);
                Dirty(item, state);
            }
            _popup.PopupEntity(Loc.GetString("surgical-sterilizer-done"), uid);
        }
    }
}

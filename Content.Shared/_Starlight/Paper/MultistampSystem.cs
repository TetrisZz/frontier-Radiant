using Content.Shared.Interaction;
using Content.Shared.Paper;
using Robust.Shared.Audio.Systems;
using Robust.Shared.Containers;

namespace Content.Shared._Starlight.Paper;

// Radiant sector: Starlight multistamp behaviour port.
public abstract partial class SharedMultistampSystem : EntitySystem
{
    [Dependency] private readonly SharedAudioSystem _audio = default!;

    public override void Initialize()
    {
        base.Initialize();
        SubscribeLocalEvent<MultistampComponent, MapInitEvent>(OnMapInit);
        SubscribeLocalEvent<MultistampComponent, ActivateInWorldEvent>(OnActivate);
        SubscribeLocalEvent<MultistampComponent, AfterAutoHandleStateEvent>(OnState);
        SubscribeLocalEvent<MultistampComponent, EntInsertedIntoContainerMessage>(OnInserted);
        SubscribeLocalEvent<MultistampComponent, EntRemovedFromContainerMessage>(OnRemoved);
    }

    private void OnState(EntityUid uid, MultistampComponent comp, ref AfterAutoHandleStateEvent args)
        => SetMultistamp(uid, comp);

    private void OnMapInit(EntityUid uid, MultistampComponent comp, MapInitEvent args)
        => SetMultistamp(uid, comp);

    private void OnInserted(EntityUid uid, MultistampComponent comp, ref EntInsertedIntoContainerMessage args)
    {
        if (!comp.Stamps.Contains(args.Entity))
            comp.Stamps.Add(args.Entity);
        SetMultistamp(uid, comp);
    }

    private void OnRemoved(EntityUid uid, MultistampComponent comp, ref EntRemovedFromContainerMessage args)
    {
        comp.Stamps.Remove(args.Entity);
        comp.CurrentEntry = comp.Stamps.Count == 0 ? 0 : Math.Clamp(comp.CurrentEntry, 0, comp.Stamps.Count - 1);
        SetMultistamp(uid, comp);
    }

    private void OnActivate(EntityUid uid, MultistampComponent comp, ActivateInWorldEvent args)
    {
        if (args.Handled || !args.Complex || comp.Stamps.Count == 0)
            return;

        comp.CurrentEntry = (comp.CurrentEntry + 1) % comp.Stamps.Count;
        Dirty(uid, comp);
        SetMultistamp(uid, comp, true, args.User);
        args.Handled = true;
    }

    public virtual void SetMultistamp(EntityUid uid, MultistampComponent comp, bool playSound = false, EntityUid? user = null)
    {
        if (!TryComp<StampComponent>(uid, out var stamp))
            return;

        if (comp.Stamps.Count == 0 || comp.CurrentEntry >= comp.Stamps.Count)
        {
            comp.CurrentStampName = Loc.GetString("multiple-tool-component-no-behavior");
            Dirty(uid, comp);
            return;
        }

        var selected = comp.Stamps[comp.CurrentEntry];
        if (!TryComp<StampComponent>(selected, out var current))
            return;

        comp.CurrentStampName = MetaData(selected).EntityName;
        stamp.StampedName = current.StampedName;
        stamp.StampedColor = current.StampedColor;
        stamp.StampState = current.StampState;
        stamp.Sound = current.Sound;

        if (playSound && comp.ChangeSound != null)
            _audio.PlayPredicted(comp.ChangeSound, uid, user);

        Dirty(uid, comp);
    }
}

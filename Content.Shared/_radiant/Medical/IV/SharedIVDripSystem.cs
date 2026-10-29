using Content.Shared.Body.Components;
using Content.Shared._DV.Chemistry.Components;
using Content.Shared.DragDrop;
using Content.Shared.Humanoid;

namespace Content.Shared._radiant.Medical.IV;

/// <summary>Draggable IV stands, with the same patient interaction model as the upstream medical stands.</summary>
public abstract class SharedIVDripSystem : EntitySystem
{
    [Dependency] private readonly SharedTransformSystem _transforms = default!;

    public override void Initialize()
    {
        SubscribeLocalEvent<IVDripComponent, CanDragEvent>(OnCanDrag);
        SubscribeLocalEvent<IVDripComponent, CanDropDraggedEvent>(OnCanDrop);
        SubscribeLocalEvent<BloodstreamComponent, CanDropTargetEvent>(OnPatientDrop);
    }

    public bool InRange(Entity<IVDripComponent> stand, EntityUid patient)
    {
        if (!Exists(patient) || TerminatingOrDeleted(patient))
            return false;
        var from = _transforms.GetMapCoordinates(stand.Owner);
        var to = _transforms.GetMapCoordinates(patient);
        return from.InRange(to, stand.Comp.Range);
    }

    public bool CanConnect(Entity<IVDripComponent> stand, EntityUid patient)
        => HasComp<BloodstreamComponent>(patient) && InRange(stand, patient)
            && (!TryComp<BlockInjectionComponent>(patient, out var blocked) || !blocked.BlockSyringe)
            && (!TryComp<HumanoidAppearanceComponent>(patient, out var humanoid) || humanoid.Species.Id != "IPC");

    private void OnCanDrag(Entity<IVDripComponent> ent, ref CanDragEvent args) => args.Handled = true;

    private void OnCanDrop(Entity<IVDripComponent> ent, ref CanDropDraggedEvent args)
    {
        args.Handled = true;
        args.CanDrop = CanConnect(ent, args.Target);
    }

    private void OnPatientDrop(Entity<BloodstreamComponent> ent, ref CanDropTargetEvent args)
    {
        if (!TryComp<IVDripComponent>(args.Dragged, out var stand))
            return;
        args.Handled = true;
        args.CanDrop = CanConnect((args.Dragged, stand), ent);
    }
}

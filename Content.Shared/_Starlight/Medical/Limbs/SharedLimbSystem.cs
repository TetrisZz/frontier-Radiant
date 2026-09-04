using Content.Shared.Body.Systems;
using Content.Shared.Body.Part;
using Robust.Shared.Network;
using Robust.Shared.Prototypes;

namespace Content.Shared._Starlight.Medical.Limbs;
// Radiant sector: concrete because Frontier does not include Starlight's server LimbSystem.
public sealed partial class SharedLimbSystem : EntitySystem
{
    [Dependency] private IPrototypeManager _prototypes = default!;
    [Dependency] private SharedBodySystem _body = default!;
    [Dependency] private INetManager _net = default!;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeLocalEvent<WithAttachedBodyPartsComponent, MapInitEvent>(OnWithAttachedBodyPartsMapInit);

    }

    private void OnWithAttachedBodyPartsMapInit(Entity<WithAttachedBodyPartsComponent> ent, ref MapInitEvent args)
    {
        // Radiant sector: the original Starlight system is abstract and its
        // concrete implementation exists only on the server. This port is a
        // concrete shared system, so without this guard the client spawned a
        // second local hand/foot in addition to the server-networked one.
        if (_net.IsClient)
            return;

        foreach (var partProtoId in ent.Comp.Parts)
        {
            if (!_prototypes.TryIndex(partProtoId.Value, out var prototype))
                continue;

            var child = Spawn(prototype.ID);
            if (!TryComp<BodyPartComponent>(child, out var childPart)
                || !_body.TryCreatePartSlotAndAttach(
                    ent.Owner,
                    partProtoId.Key,
                    child,
                    childPart.PartType))
            {
                QueueDel(child);
            }
        }
    }
}

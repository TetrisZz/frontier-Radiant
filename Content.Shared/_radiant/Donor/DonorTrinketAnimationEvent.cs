using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Donor;

[Serializable, NetSerializable]
public sealed class DonorTrinketAnimationEvent(NetEntity entity) : EntityEventArgs
{
    public readonly NetEntity Entity = entity;
}

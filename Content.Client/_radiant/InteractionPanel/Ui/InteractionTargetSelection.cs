using System.Linq;
using Robust.Shared.Map;

namespace Content.Client.Interaction.Panel.Ui;

public enum InteractionTargetStatus : byte
{
    None,
    Unavailable,
    TooFar,
    Obstructed,
    Ready
}

public static class InteractionTargetSelection
{
    public const float Range = 2f;

    public static bool InRange(MapCoordinates user, MapCoordinates target)
        => user.MapId != MapId.Nullspace && user.MapId == target.MapId
           && (user.Position - target.Position).LengthSquared() <= Range * Range;

    public static EntityUid? Resolve(EntityUid? selected, IReadOnlyList<EntityUid> nearestFirst)
    {
        // An explicitly chosen partner must never silently turn into someone else.
        if (selected is { } partner)
            return nearestFirst.Contains(partner) ? partner : null;

        return nearestFirst.Count > 0 ? nearestFirst[0] : null;
    }

    public static InteractionTargetStatus Status(MapCoordinates user, MapCoordinates? target, bool valid, bool unobstructed)
    {
        if (target is not { } position)
            return InteractionTargetStatus.None;
        if (!valid || user.MapId == MapId.Nullspace || user.MapId != position.MapId)
            return InteractionTargetStatus.Unavailable;
        if (!InRange(user, position))
            return InteractionTargetStatus.TooFar;
        return unobstructed ? InteractionTargetStatus.Ready : InteractionTargetStatus.Obstructed;
    }
}

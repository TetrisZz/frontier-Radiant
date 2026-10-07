using Content.Shared.FixedPoint;

namespace Content.Shared.Body.Events;

[ByRefEvent]
public record struct BloodRegenerationEvent(FixedPoint2 Amount);

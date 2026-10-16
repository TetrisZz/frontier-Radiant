namespace Content.Shared._radiant.Careers;

[ByRefEvent]
public readonly record struct CareerConstructionCompletedEvent(EntityUid Entity, EntityUid? User, EntityUid? Previous = null);

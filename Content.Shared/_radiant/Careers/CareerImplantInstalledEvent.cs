namespace Content.Shared._radiant.Careers;

[ByRefEvent]
public readonly record struct CareerImplantInstalledEvent(EntityUid User, EntityUid Patient, EntityUid Implant);

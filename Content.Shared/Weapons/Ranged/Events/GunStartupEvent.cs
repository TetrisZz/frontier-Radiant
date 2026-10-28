namespace Content.Shared.Weapons.Ranged.Events;

/// <summary>Server-side gun startup, after rebuilding derived weapon stats, including restored guns.</summary>
[ByRefEvent]
public readonly record struct GunStartupEvent;

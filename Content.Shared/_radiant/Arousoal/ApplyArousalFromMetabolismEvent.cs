namespace Content.Shared._radiant.Arousal;

/// <summary>
/// Raised on the mob when a reagent metabolism effect should add arousal (handled on server).
/// </summary>
[ByRefEvent]
public readonly record struct ApplyArousalFromMetabolismEvent(float Amount);

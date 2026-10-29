using Content.Shared.Chemistry.Components;

namespace Content.Shared.Chemistry;

/// <summary>
/// Raised on a dumping destination after the source interaction has been validated.
/// A destination with special transfer rules can handle the transfer itself.
/// </summary>
[ByRefEvent]
public record struct DumpSolutionEvent(EntityUid User, Entity<SolutionComponent> Source, Entity<SolutionComponent> Target)
{
    public bool Handled;
    public bool Transferred;
}

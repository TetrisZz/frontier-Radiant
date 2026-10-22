using System;

namespace Content.Server._radiant.ResearchBlueprints;

[RegisterComponent]
public sealed partial class BlueprintResearchPrintingComponent : Component
{
    public EntityUid ServerUid;

    public TimeSpan StartTime;
    public TimeSpan FinishTime;
    public TimeSpan PrintSoundTime;
    public TimeSpan NextUiUpdate;
    public TimeSpan Duration;

    public bool PrintSoundPlayed;
}

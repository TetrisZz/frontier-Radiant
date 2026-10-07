namespace Content.Server.Fluids.Components;

[RegisterComponent]
public sealed partial class FootstepTrackComponent : Component
{
    public EntityUid? LastGrid;
    public Vector2i? LastTile;
    public int RemainingSteps;
    public Color TrackColor = Color.White;
    public bool BloodOnFeet;
    public bool WaterOnFeet;
    public bool AlternateStep;
    public TimeSpan NextSplatter;
}

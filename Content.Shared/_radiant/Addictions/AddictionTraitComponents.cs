namespace Content.Shared._radiant.Addictions;

/// <summary>A permanent roleplay habit, independent of acquired, treatable dependence.</summary>
[RegisterComponent]
public sealed partial class PermanentHabitComponent : Component
{
    [DataField] public string Group = "nicotine";
    [DataField] public float SecondsWithoutDose;
    [DataField] public float MessageTimer;
    [DataField] public float CravingDelay = 600;
    [DataField] public float MessageInterval = 180;
}

/// <summary>Starting history chosen in the lobby, not persistent addiction progress.</summary>
[RegisterComponent]
public sealed partial class AddictionHistoryComponent : Component
{
    [DataField] public string Group = "nicotine";
    [DataField] public float Dependence = 55;
    [DataField] public float Tolerance = 35;
    [DataField] public bool Recovering;
}

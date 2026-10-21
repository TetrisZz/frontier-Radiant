using Robust.Shared.GameStates;
using Robust.Shared.Serialization.TypeSerializers.Implementations.Custom;

namespace Content.Shared._radiant.Fishing.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true), AutoGenerateComponentPause]
public sealed partial class FishBreedingComponent : Component
{
    [DataField] public bool Initialized;
    [DataField, AutoNetworkedField] public bool Female;
    [DataField, AutoNetworkedField] public bool Juvenile;
    [DataField, AutoNetworkedField] public int FirstAllele;
    [DataField, AutoNetworkedField] public int SecondAllele;
    [DataField] public float GrowthSeconds;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan ReadyAt;

    public Color Tint => FishBreedingRules.Tint(FirstAllele, SecondAllele);
}

[RegisterComponent, AutoGenerateComponentPause]
public sealed partial class AquariumBreedingComponent : Component
{
    [DataField] public float Interval = 240;
    [DataField(customTypeSerializer: typeof(TimeOffsetSerializer)), AutoPausedField] public TimeSpan NextCheck;
}

public static class FishBreedingRules
{
    public const float GrowthDuration = 300;
    public static bool Compatible(string first, string second, bool firstFemale, bool secondFemale,
        bool firstJuvenile, bool secondJuvenile)
        => first == second && firstFemale != secondFemale && !firstJuvenile && !secondJuvenile;

    public static Color Tint(int first, int second)
        => first == second ? first switch
        {
            1 => Color.FromHex("#FFD0A8"),
            2 => Color.FromHex("#B9DAFF"),
            _ => Color.White,
        } : Color.White;
}

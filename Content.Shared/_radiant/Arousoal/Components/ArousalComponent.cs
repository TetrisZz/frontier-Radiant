using Robust.Shared.GameStates;
using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Arousal.Components;

[RegisterComponent, NetworkedComponent, AutoGenerateComponentState(true)]
public sealed partial class ArousalComponent : Component
{
    [DataField, AutoNetworkedField]
    public float CurrentArousal;

    [DataField, AutoNetworkedField]
    public float MaxArousal = 44f;

    [DataField, AutoNetworkedField]
    public float DecayPerSecond = 1.0f;

    [DataField, AutoNetworkedField]
    public TimeSpan ClimaxCooldown = TimeSpan.FromSeconds(8);

    [DataField, AutoNetworkedField]
    public TimeSpan NextClimaxAt = TimeSpan.Zero;

    [DataField, AutoNetworkedField]
    public ArousalVisualCurve VisualCurve = ArousalVisualCurve.SmoothStep;

    [DataField, AutoNetworkedField]
    public ArousalState State = ArousalState.Calm;
}

[RegisterComponent]
public sealed partial class ArousalGenderConfigComponent : Component
{
    [DataField]
    public ArousalGenderConfig Male = new()
    {
        GainMultiplier = 1f,
        DecayMultiplier = 1f,
        // Emote prototype id; male/female moan sounds come from speech_emote_sounds (MaleSton/FemaleSton collections).
        ClimaxEmoteId = "Ston",
        EnableFluidEffect = true
    };

    [DataField]
    public ArousalGenderConfig Female = new()
    {
        GainMultiplier = 1f,
        DecayMultiplier = 1f,
        ClimaxEmoteId = "Ston",
        EnableFluidEffect = false
    };

    [DataField]
    public ArousalGenderConfig Fallback = new()
    {
        GainMultiplier = 1f,
        DecayMultiplier = 1f,
        ClimaxEmoteId = "Ston",
        EnableFluidEffect = false
    };
}

[DataDefinition, Serializable, NetSerializable]
public sealed partial class ArousalGenderConfig
{
    [DataField]
    public float GainMultiplier = 1f;

    [DataField]
    public float DecayMultiplier = 1f;

    [DataField]
    public string ClimaxEmoteId = "Ston";

    [DataField]
    public bool EnableFluidEffect;
}

[Serializable, NetSerializable]
public enum ArousalState : byte
{
    Calm,
    Rising,
    ClimaxCooldown
}

[Serializable, NetSerializable]
public enum ArousalVisualCurve : byte
{
    Linear,
    SmoothStep,
    Pow2,
    Pow3
}


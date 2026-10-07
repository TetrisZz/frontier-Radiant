using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Addictions;

[Prototype("addictionGroup")]
public sealed partial class AddictionGroupPrototype : IPrototype
{
    [IdDataField] public string ID { get; private set; } = default!;
    [DataField] public List<string> Reagents = new();
    [DataField] public bool Alcohol;
    [DataField] public bool RequiresErp;
    [DataField] public float ToleranceGain = 3;
    [DataField] public float DependenceGain = 2;
    [DataField] public float Clearance = 0.02f;
    [DataField] public float ToxicThreshold = 20;
    [DataField] public float GraceSeconds = 300;
    [DataField] public float StageSeconds = 300;
    [DataField] public float SpeedPenalty = 0.03f;
    [DataField] public float Tremor;
}

/// <summary>Round-local state, separated from ECS for testing and future profile persistence.</summary>
[DataDefinition]
public sealed partial class AddictionState
{
    [DataField] public float Dependence;
    [DataField] public float Tolerance;
    [DataField] public float Intoxication;
    [DataField] public float SecondsWithoutDose;
    [DataField] public int WithdrawalStage;
}

public static class AddictionRules
{
    public static void Dose(AddictionState state, AddictionGroupPrototype group, float amount)
    {
        if (!float.IsFinite(amount) || amount <= 0)
            return;
        state.Dependence = Math.Clamp(state.Dependence + amount * group.DependenceGain * (0.2f + state.Tolerance / 100), 0, 100);
        state.Tolerance = Math.Clamp(state.Tolerance + amount * group.ToleranceGain * (1 - state.Tolerance / 100), 0, 100);
        state.Intoxication = Math.Min(200, state.Intoxication + amount);
        state.SecondsWithoutDose = 0;
        state.WithdrawalStage = 0;
    }

    public static void Advance(AddictionState state, AddictionGroupPrototype group, float seconds, bool treatment)
    {
        if (!float.IsFinite(seconds) || seconds <= 0)
            return;
        state.SecondsWithoutDose += seconds;
        state.Intoxication = Math.Max(0, state.Intoxication - group.Clearance * seconds);
        // A short gap between metabolism ticks must not count as recovery.
        if (state.SecondsWithoutDose > group.GraceSeconds)
        {
            state.Tolerance = Math.Max(0, state.Tolerance - seconds * 0.06f);
            state.Dependence = Math.Max(0, state.Dependence - seconds * (treatment ? 0.0375f : 0.025f));
        }
        var elapsed = state.SecondsWithoutDose - group.GraceSeconds;
        var severity = state.Dependence < 20 ? 0 : state.Dependence < 40 ? 2 : state.Dependence < 70 ? 3 : 4;
        var step = Math.Max(1, group.StageSeconds);
        var stage = elapsed < 0 ? 0 : elapsed < step ? 1 : elapsed < 2 * step ? 2 : elapsed < 4 * step ? 3 : 4;
        state.WithdrawalStage = Math.Min(severity, stage);
    }

    public static float Strength(float tolerance) => Interpolate(tolerance, new[] { 1f, .95f, .85f, .7f, .55f });
    private static float Interpolate(float tolerance, float[] values)
    {
        var point = Math.Clamp(tolerance, 0, 100) / 25;
        var index = Math.Min(3, (int) point);
        return values[index] + (values[index + 1] - values[index]) * (point - index);
    }
}

using Robust.Shared.Serialization;

namespace Content.Shared._radiant.Medical.Genetics;

[Serializable, NetSerializable]
public enum GeneticTherapyVariant : byte { Standard, Stable, Economical, Temporary }

/// <summary>Variants trade effectiveness, load or duration; none increases a gene's base effect.</summary>
public static class GeneticTherapyBalance
{
    public static int Load(int original, GeneticTherapyVariant variant)
        => original + (variant == GeneticTherapyVariant.Stable ? 10 : 0);
    public static float Effect(GeneticTherapyVariant variant)
        => variant == GeneticTherapyVariant.Economical ? 0.7f : 1;
    public static float Nutrition(GeneticTherapyVariant variant)
        => variant == GeneticTherapyVariant.Economical ? 0.5f : 1;
    public static float Risk(float original, GeneticTherapyVariant variant)
        => Math.Clamp(original * (variant == GeneticTherapyVariant.Stable ? 0.25f : 1), 0, 1);
    public const float TemporarySeconds = 1200;
    public const float RecoverySeconds = 180;
    public const float RestSeconds = 30;
}

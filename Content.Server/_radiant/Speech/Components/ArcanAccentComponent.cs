namespace Content.Server._radiant.Speech.Components;

[RegisterComponent]
public sealed partial class ArcanAccentComponent : Component
{
    /// <summary>
    ///     Chance that the message will be appended with "♥"
    /// </summary>
    [DataField("heartChance")]
    public float heartChance = 0.1f;
}

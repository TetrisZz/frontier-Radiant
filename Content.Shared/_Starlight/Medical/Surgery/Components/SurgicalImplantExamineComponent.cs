namespace Content.Shared._Starlight.Medical.Surgery.Components;

/// <summary>
/// A subtle externally visible clue shown while this implant or cyberlimb is installed.
/// </summary>
[RegisterComponent]
public sealed partial class SurgicalImplantExamineComponent : Component
{
    [DataField(required: true)]
    public string Text = string.Empty;

    /// <summary>
    /// Radiant sector: optional model-specific hints selected by a substring of
    /// the installed entity prototype. This lets a common cyberlimb parent cover
    /// every left/right body-part variant without duplicating components.
    /// </summary>
    [DataField]
    public Dictionary<string, string> PrototypeHints = new();

    /// <summary>
    /// Radiant sector: how readily this augmentation and its purpose can be identified.
    /// Controls the color used in the dedicated augmentation examination window.
    /// </summary>
    [DataField]
    public SurgicalAugmentVisibility Visibility = SurgicalAugmentVisibility.Subtle;

    /// <summary>
    /// Radiant sector: model-specific visibility overrides matching <see cref="PrototypeHints"/>.
    /// </summary>
    [DataField]
    public Dictionary<string, SurgicalAugmentVisibility> PrototypeVisibilities = new();
}

public enum SurgicalAugmentVisibility : byte
{
    Concealed,
    Subtle,
    Noticeable,
    Obvious,
    Combat,
}

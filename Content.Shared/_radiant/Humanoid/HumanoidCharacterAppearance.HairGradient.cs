using Content.Shared._radiant.Humanoid;
using Robust.Shared.Serialization;

namespace Content.Shared.Humanoid;

/// <summary>
/// Поля градиента волос <see cref="HumanoidCharacterAppearance"/>.
/// </summary>
public sealed partial class HumanoidCharacterAppearance
{
    [DataField]
    public HairColoringMode HairColoringMode { get; set; } = HairColoringMode.Solid;

    [DataField]
    public Color HairGradientColor { get; set; } = Color.White;

    [DataField]
    public HairGradientDirection HairGradientDirection { get; set; } = HairGradientDirection.TopToBottom;

    [DataField]
    public HairColoringMode FacialHairColoringMode { get; set; } = HairColoringMode.Solid;

    [DataField]
    public Color FacialHairGradientColor { get; set; } = Color.White;

    [DataField]
    public HairGradientDirection FacialHairGradientDirection { get; set; } = HairGradientDirection.TopToBottom;

    public void CopyGradientFrom(HumanoidCharacterAppearance other)
    {
        HairColoringMode = other.HairColoringMode;
        HairGradientColor = other.HairGradientColor;
        HairGradientDirection = other.HairGradientDirection;
        FacialHairColoringMode = other.FacialHairColoringMode;
        FacialHairGradientColor = other.FacialHairGradientColor;
        FacialHairGradientDirection = other.FacialHairGradientDirection;
    }

    public HumanoidCharacterAppearance WithHairGradient(
        HairColoringMode mode,
        Color gradientColor,
        HairGradientDirection direction)
    {
        var appearance = new HumanoidCharacterAppearance(this);
        appearance.HairColoringMode = mode;
        appearance.HairGradientColor = ClampColor(gradientColor);
        appearance.HairGradientDirection = direction;
        return appearance;
    }

    public HumanoidCharacterAppearance WithFacialHairGradient(
        HairColoringMode mode,
        Color gradientColor,
        HairGradientDirection direction)
    {
        var appearance = new HumanoidCharacterAppearance(this);
        appearance.FacialHairColoringMode = mode;
        appearance.FacialHairGradientColor = ClampColor(gradientColor);
        appearance.FacialHairGradientDirection = direction;
        return appearance;
    }

    private static void ApplyValidatedGradient(
        HumanoidCharacterAppearance target,
        HairColoringMode hairMode,
        Color hairGradientColor,
        HairGradientDirection hairDirection,
        HairColoringMode facialMode,
        Color facialGradientColor,
        HairGradientDirection facialDirection)
    {
        target.HairColoringMode = hairMode;
        target.HairGradientColor = hairGradientColor;
        target.HairGradientDirection = hairDirection;
        target.FacialHairColoringMode = facialMode;
        target.FacialHairGradientColor = facialGradientColor;
        target.FacialHairGradientDirection = facialDirection;
    }
}

using System.Linq;
using Content.Shared.Humanoid;
using Content.Shared.Humanoid.Markings;
using Content.Shared.Humanoid.Prototypes;
using Robust.Shared.Prototypes;

namespace Content.Shared._radiant.Humanoid;

public static class HairColoringHelper
{
    public const string GradientShader = "HairGradient";

    public static bool CanUseCustomHairColor(
        string species,
        HumanoidVisualLayers layer,
        MarkingManager markingManager,
        IPrototypeManager proto)
    {
        return !markingManager.MustMatchSkin(species, layer, out _, proto)
               && markingManager.MustMatchColor(species, layer, out _, proto) == null;
    }

    public static Color ResolvePrimaryColor(
        HumanoidCharacterAppearance appearance,
        string species,
        HumanoidVisualLayers layer,
        MarkingManager markingManager,
        IPrototypeManager proto)
    {
        if (markingManager.MustMatchSkin(species, layer, out var alpha, proto))
            return appearance.SkinColor.WithAlpha(alpha);

        if (markingManager.MustMatchColor(species, layer, out var forcedAlpha, proto) is Color forced)
            return forced.WithAlpha(forcedAlpha);

        return layer == HumanoidVisualLayers.Hair
            ? appearance.HairColor
            : appearance.FacialHairColor;
    }

    public static HairColoringMode GetColoringMode(HumanoidCharacterAppearance appearance, HumanoidVisualLayers layer)
    {
        return layer == HumanoidVisualLayers.Hair
            ? appearance.HairColoringMode
            : appearance.FacialHairColoringMode;
    }

    public static Color GetGradientColor(HumanoidCharacterAppearance appearance, HumanoidVisualLayers layer)
    {
        return layer == HumanoidVisualLayers.Hair
            ? appearance.HairGradientColor
            : appearance.FacialHairGradientColor;
    }

    public static HairGradientDirection GetGradientDirection(HumanoidCharacterAppearance appearance, HumanoidVisualLayers layer)
    {
        return layer == HumanoidVisualLayers.Hair
            ? appearance.HairGradientDirection
            : appearance.FacialHairGradientDirection;
    }

    public static List<Color> BuildMarkingColors(
        HumanoidCharacterAppearance appearance,
        string species,
        MarkingPrototype prototype,
        HumanoidVisualLayers layer,
        MarkingManager markingManager,
        IPrototypeManager proto)
    {
        var primary = ResolvePrimaryColor(appearance, species, layer, markingManager, proto);
        var layerCount = Math.Max(1, prototype.Sprites.Count);

        if (!CanUseCustomHairColor(species, layer, markingManager, proto)
            || GetColoringMode(appearance, layer) == HairColoringMode.Solid)
        {
            return Enumerable.Repeat(primary, layerCount).ToList();
        }

        return Enumerable.Repeat(primary, layerCount).ToList();
    }

    public static List<Color> InterpolateLayerColors(
        Color primary,
        Color secondary,
        HairGradientDirection direction,
        int layerCount)
    {
        var colors = new List<Color>(layerCount);

        for (var i = 0; i < layerCount; i++)
        {
            var t = layerCount == 1 ? 0f : (float) i / (layerCount - 1);

            if (ShouldReverseForLayers(direction))
                t = 1f - t;

            colors.Add(Color.InterpolateBetween(primary, secondary, t));
        }

        return colors;
    }

    public static float DirectionToShaderParam(HairGradientDirection direction) => (float) direction;

    public static void CopyGradientSettingsToComponent(
        HumanoidCharacterAppearance appearance,
        HumanoidAppearanceComponent humanoid)
    {
        humanoid.HairColoringMode = appearance.HairColoringMode;
        humanoid.HairGradientColor = appearance.HairGradientColor;
        humanoid.HairGradientDirection = appearance.HairGradientDirection;
        humanoid.FacialHairColoringMode = appearance.FacialHairColoringMode;
        humanoid.FacialHairGradientColor = appearance.FacialHairGradientColor;
        humanoid.FacialHairGradientDirection = appearance.FacialHairGradientDirection;
    }

    private static bool ShouldReverseForLayers(HairGradientDirection direction)
    {
        return direction is HairGradientDirection.RightToLeft
            or HairGradientDirection.BottomToTop
            or HairGradientDirection.BottomLeftToTopRight;
    }
}

using Content.Shared._radiant.Humanoid;
using Content.Shared.Humanoid;

namespace Content.Server.Humanoid;

public sealed partial class HumanoidAppearanceSystem
{
    public void SetHairGradient(
        EntityUid uid,
        HumanoidVisualLayers layer,
        HairColoringMode mode,
        Color gradientColor,
        HairGradientDirection direction,
        HumanoidAppearanceComponent? humanoid = null)
    {
        if (!Resolve(uid, ref humanoid))
            return;

        switch (layer)
        {
            case HumanoidVisualLayers.Hair:
                humanoid!.HairColoringMode = mode;
                humanoid.HairGradientColor = gradientColor;
                humanoid.HairGradientDirection = direction;
                break;
            case HumanoidVisualLayers.FacialHair:
                humanoid!.FacialHairColoringMode = mode;
                humanoid.FacialHairGradientColor = gradientColor;
                humanoid.FacialHairGradientDirection = direction;
                break;
            default:
                return;
        }

        Dirty(uid, humanoid);
    }

    public void CopyHairGradient(
        HumanoidAppearanceComponent source,
        HumanoidAppearanceComponent target)
    {
        target.HairColoringMode = source.HairColoringMode;
        target.HairGradientColor = source.HairGradientColor;
        target.HairGradientDirection = source.HairGradientDirection;
        target.FacialHairColoringMode = source.FacialHairColoringMode;
        target.FacialHairGradientColor = source.FacialHairGradientColor;
        target.FacialHairGradientDirection = source.FacialHairGradientDirection;
    }
}
